using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using Microsoft.UI.Text;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace GameShelf;

public sealed partial class MainPage : Page
{
    private readonly DatabaseService _dbService;
    private readonly LaunchService _launchService;

    public ObservableCollection<Game> FilteredGames { get; } = new();
    public List<Game> AllGames { get; } = new();

    public MainPage()
    {
        InitializeComponent();

        _dbService = new DatabaseService();
        _launchService = new LaunchService(_dbService);

        LoadGames();
    }

    private void LoadGames()
    {
        AllGames.Clear();
        var games = _dbService.GetAllGames();
        foreach (var game in games)
        {
            AllGames.Add(game);
        }
        ApplyFilters();
        RefreshTagsFilterCombo();
    }

    private void ApplyFilters()
    {
        var searchText = SearchBox.Text.Trim();
        var selectedTagItem = TagFilterCombo.SelectedItem;
        string selectedTag = "All Tags";
        
        if (selectedTagItem is ComboBoxItem cbi)
        {
            selectedTag = cbi.Content as string ?? "All Tags";
        }
        else if (selectedTagItem is string s)
        {
            selectedTag = s;
        }

        var filtered = AllGames.AsEnumerable();

        if (!string.IsNullOrEmpty(searchText))
        {
            filtered = filtered.Where(g => 
                g.Title.Contains(searchText, StringComparison.OrdinalIgnoreCase) || 
                (g.Tags != null && g.Tags.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            );
        }

        if (!string.Equals(selectedTag, "All Tags", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(g => g.TagList.Contains(selectedTag, StringComparer.OrdinalIgnoreCase));
        }

        FilteredGames.Clear();
        foreach (var game in filtered)
        {
            FilteredGames.Add(game);
        }
    }

    private void RefreshTagsFilterCombo()
    {
        var selectedTag = "All Tags";
        if (TagFilterCombo.SelectedItem is ComboBoxItem cbi)
        {
            selectedTag = cbi.Content as string ?? "All Tags";
        }
        else if (TagFilterCombo.SelectedItem is string s)
        {
            selectedTag = s;
        }

        TagFilterCombo.SelectionChanged -= TagFilterCombo_SelectionChanged;
        TagFilterCombo.Items.Clear();
        
        var allTagsItem = new ComboBoxItem { Content = "All Tags" };
        TagFilterCombo.Items.Add(allTagsItem);
        TagFilterCombo.SelectedItem = allTagsItem;

        var uniqueTags = AllGames
            .SelectMany(g => g.TagList)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t)
            .ToList();

        foreach (var tag in uniqueTags)
        {
            TagFilterCombo.Items.Add(tag);
            if (string.Equals(tag, selectedTag, StringComparison.OrdinalIgnoreCase))
            {
                TagFilterCombo.SelectedItem = tag;
            }
        }

        TagFilterCombo.SelectionChanged += TagFilterCombo_SelectionChanged;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilters();
    }

    private void TagFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyFilters();
    }

    private void GamesGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Game game)
        {
            LaunchGame(game);
        }
    }

    private void LaunchGame_MenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is Game game)
        {
            LaunchGame(game);
        }
    }

    private DateTime _lastLaunchTime = DateTime.MinValue;

    private async void LaunchGame(Game game)
    {
        if (DateTime.UtcNow - _lastLaunchTime < TimeSpan.FromSeconds(1))
        {
            return;
        }
        _lastLaunchTime = DateTime.UtcNow;

        if (game.IsRunning)
        {
            var dialog = new ContentDialog
            {
                Title = "Game Already Running",
                Content = $"'{game.Title}' is already running. Please close the running instance first.",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
            return;
        }

        try
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _launchService.LaunchGameAsync(game, this.DispatcherQueue,
                        onStatusChanged: (status) => { },
                        onPlayTimeUpdated: (playtime) => { }
                    );
                }
                catch (Exception ex)
                {
                    DispatcherQueue.TryEnqueue(async () =>
                    {
                        var errorDialog = new ContentDialog
                        {
                            Title = "Error Launching Game",
                            Content = $"An error occurred while launching '{game.Title}':\n{ex.Message}",
                            CloseButtonText = "OK",
                            XamlRoot = this.XamlRoot
                        };
                        await errorDialog.ShowAsync();
                    });
                }
            });
        }
        catch (Exception ex)
        {
            var errorDialog = new ContentDialog
            {
                Title = "Error Initiating Launch",
                Content = $"Could not launch game:\n{ex.Message}",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await errorDialog.ShowAsync();
        }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var config = ConfigService.LoadConfig();

        var dialog = new ContentDialog
        {
            Title = "Settings",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var stack = new StackPanel { Spacing = 12, Width = 380 };

        var apiKeyBox = new PasswordBox
        {
            Header = "SteamGridDB API Key",
            Password = config.SteamGridDbApiKey,
            PasswordChar = "*"
        };

        var linkButton = new HyperlinkButton
        {
            Content = "Get your API Key from SteamGridDB",
            NavigateUri = new Uri("https://www.steamgriddb.com/profile/api"),
            Margin = new Thickness(0, 4, 0, 0)
        };

        stack.Children.Add(apiKeyBox);
        stack.Children.Add(linkButton);

        dialog.Content = stack;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            config.SteamGridDbApiKey = apiKeyBox.Password.Trim();
            ConfigService.SaveConfig(config);
        }
    }

    private async void AddGame_Click(object sender, RoutedEventArgs e)
    {
        var folderPicker = new FolderPicker();
        folderPicker.FileTypeFilter.Add("*");
        
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.StartupWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
        
        var folder = await folderPicker.PickSingleFolderAsync();
        if (folder == null)
        {
            return; // Abort entirely
        }
        
        string selectedFolder = folder.Path;
        var candidates = GetExeCandidates(selectedFolder);
        if (candidates.Count == 0)
        {
            var errorDialog = new ContentDialog
            {
                Title = "No Executables Found",
                Content = $"We could not find any executables in the folder:\n{selectedFolder}\n\nPlease select a folder containing the game executable.",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await errorDialog.ShowAsync();
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Add Game Details",
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = candidates.Count == 1,
            XamlRoot = this.XamlRoot
        };

        var stack = new StackPanel { Spacing = 12, Width = 380 };

        var folderLabel = new TextBlock { Text = "Selected Folder:", FontWeight = FontWeights.SemiBold };
        var folderPathText = new TextBlock { Text = selectedFolder, Foreground = App.Current.Resources["TextControlForeground"] as Brush, TextWrapping = TextWrapping.Wrap };

        var titleBox = new TextBox { Header = "Game Title", Text = CleanFolderTitle(selectedFolder) };
        
        var fetchCoverBtn = new Button
        {
            Content = "Fetch Cover Art",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 8)
        };
        var fetchCoverTooltip = new ToolTip();
        ToolTipService.SetToolTip(fetchCoverBtn, fetchCoverTooltip);

        var tagsBox = new TextBox { Header = "Tags (comma-separated)" };

        var exeLabel = new TextBlock { Text = "Select Executable:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) };
        var exeCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        
        var relativePaths = candidates.Select(c => Path.GetRelativePath(selectedFolder, c)).ToList();
        exeCombo.ItemsSource = relativePaths;
        exeCombo.SelectedIndex = 0;
        string selectedRelativeExe = relativePaths[0];

        stack.Children.Add(folderLabel);
        stack.Children.Add(folderPathText);
        stack.Children.Add(titleBox);
        stack.Children.Add(fetchCoverBtn);
        stack.Children.Add(tagsBox);
        stack.Children.Add(exeLabel);
        stack.Children.Add(exeCombo);

        CheckBox confirmCheck = null;
        if (candidates.Count > 1)
        {
            confirmCheck = new CheckBox
            {
                Content = "Confirm selected executable is correct",
                Margin = new Thickness(0, 4, 0, 0)
            };
            stack.Children.Add(confirmCheck);
        }

        dialog.Content = stack;

        string selectedTempCoverPath = "";

        var config = ConfigService.LoadConfig();
        bool hasApiKey = !string.IsNullOrWhiteSpace(config.SteamGridDbApiKey);

        Action updateFetchButtonState = () =>
        {
            bool hasTitle = !string.IsNullOrWhiteSpace(titleBox.Text);
            if (!hasApiKey)
            {
                fetchCoverBtn.IsEnabled = false;
                fetchCoverTooltip.Content = "Please configure your SteamGridDB API key in Settings first.";
            }
            else if (!hasTitle)
            {
                fetchCoverBtn.IsEnabled = false;
                fetchCoverTooltip.Content = "Please enter a game title first.";
            }
            else
            {
                fetchCoverBtn.IsEnabled = true;
                fetchCoverTooltip.Content = "Fetch vertical cover art candidates from SteamGridDB.";
            }
        };

        Action updateAddButtonState = () =>
        {
            bool hasTitle = !string.IsNullOrWhiteSpace(titleBox.Text);
            bool hasExe = exeCombo.SelectedItem != null;
            bool isConfirmed = candidates.Count == 1 || (confirmCheck != null && confirmCheck.IsChecked == true);
            dialog.IsPrimaryButtonEnabled = hasTitle && hasExe && isConfirmed;
        };

        titleBox.TextChanged += (s, args) =>
        {
            updateFetchButtonState();
            updateAddButtonState();
        };

        exeCombo.SelectionChanged += (s, args) =>
        {
            if (exeCombo.SelectedItem is string relativePath)
            {
                selectedRelativeExe = relativePath;
                if (candidates.Count > 1 && confirmCheck != null)
                {
                    confirmCheck.IsChecked = false;
                }
            }
            updateAddButtonState();
        };

        if (confirmCheck != null)
        {
            confirmCheck.Checked += (s, args) => updateAddButtonState();
            confirmCheck.Unchecked += (s, args) => updateAddButtonState();
        }

        updateFetchButtonState();
        updateAddButtonState();

        bool shouldFetchCover = false;
        fetchCoverBtn.Click += (s, args) =>
        {
            shouldFetchCover = true;
            dialog.Hide();
        };

        bool isDialogActive = true;
        while (isDialogActive)
        {
            shouldFetchCover = false;
            var result = await dialog.ShowAsync();

            if (shouldFetchCover)
            {
                var tempCover = await FetchOrUploadCoverAsync(titleBox.Text, config.SteamGridDbApiKey);
                if (!string.IsNullOrEmpty(tempCover))
                {
                    if (!string.IsNullOrEmpty(selectedTempCoverPath) && File.Exists(selectedTempCoverPath))
                    {
                        try { File.Delete(selectedTempCoverPath); } catch { }
                    }
                    selectedTempCoverPath = tempCover;
                    fetchCoverBtn.Content = "Cover Art Selected ✓";
                }
            }
            else if (result == ContentDialogResult.Primary && !string.IsNullOrEmpty(selectedFolder) && !string.IsNullOrEmpty(selectedRelativeExe))
            {
                var newGame = new Game
                {
                    Title = titleBox.Text.Trim(),
                    Folder = selectedFolder,
                    ExePath = selectedRelativeExe,
                    Tags = tagsBox.Text
                };

                _dbService.InsertGame(newGame);

                if (!string.IsNullOrEmpty(selectedTempCoverPath))
                {
                    var finalPath = Path.Combine(_dbService.CoversDirectory, $"{newGame.Id}.jpg");
                    try
                    {
                        if (File.Exists(selectedTempCoverPath))
                        {
                            if (File.Exists(finalPath))
                            {
                                File.Delete(finalPath);
                            }
                            File.Move(selectedTempCoverPath, finalPath);
                            newGame.CoverPath = finalPath;
                            _dbService.UpdateGame(newGame);
                        }
                    }
                    catch { }
                }

                AllGames.Add(newGame);
                ApplyFilters();
                RefreshTagsFilterCombo();
                isDialogActive = false;
            }
            else
            {
                if (!string.IsNullOrEmpty(selectedTempCoverPath) && File.Exists(selectedTempCoverPath))
                {
                    try { File.Delete(selectedTempCoverPath); } catch { }
                }
                isDialogActive = false;
            }
        }
    }

    private async void EditGame_MenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is Game game)
        {
            await EditGameAsync(game);
        }
    }

    private async Task EditGameAsync(Game game)
    {
        var dialog = new ContentDialog
        {
            Title = "Edit Game Details",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var stack = new StackPanel { Spacing = 12, Width = 380 };

        var titleBox = new TextBox { Header = "Game Title", Text = game.Title };
        
        var fetchCoverBtn = new Button
        {
            Content = "Fetch Cover Art",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 8)
        };
        var fetchCoverTooltip = new ToolTip();
        ToolTipService.SetToolTip(fetchCoverBtn, fetchCoverTooltip);

        var config = ConfigService.LoadConfig();
        bool hasApiKey = !string.IsNullOrWhiteSpace(config.SteamGridDbApiKey);

        Action updateFetchButtonState = () =>
        {
            bool hasTitle = !string.IsNullOrWhiteSpace(titleBox.Text);
            if (!hasApiKey)
            {
                fetchCoverBtn.IsEnabled = false;
                fetchCoverTooltip.Content = "Please configure your SteamGridDB API key in Settings first.";
            }
            else if (!hasTitle)
            {
                fetchCoverBtn.IsEnabled = false;
                fetchCoverTooltip.Content = "Please enter a game title first.";
            }
            else
            {
                fetchCoverBtn.IsEnabled = true;
                fetchCoverTooltip.Content = "Fetch vertical cover art candidates from SteamGridDB.";
            }
        };

        titleBox.TextChanged += (s, args) => updateFetchButtonState();
        updateFetchButtonState();

        var tagsBox = new TextBox { Header = "Tags (comma-separated)", Text = game.Tags };

        var pathLabel = new TextBlock { Text = "Path Settings", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 0) };
        var folderText = new TextBlock { Text = $"Folder: {game.Folder}", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray) };
        var exeText = new TextBlock { Text = $"Executable: {game.ExePath}", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray) };

        var repairBtn = new Button { Content = "Repair Path..." };

        stack.Children.Add(titleBox);
        stack.Children.Add(fetchCoverBtn);
        stack.Children.Add(tagsBox);
        stack.Children.Add(pathLabel);
        stack.Children.Add(folderText);
        stack.Children.Add(exeText);
        stack.Children.Add(repairBtn);

        dialog.Content = stack;

        bool shouldFetchCover = false;
        bool shouldRepairPath = false;

        fetchCoverBtn.Click += (s, args) =>
        {
            shouldFetchCover = true;
            dialog.Hide();
        };

        repairBtn.Click += (s, args) =>
        {
            shouldRepairPath = true;
            dialog.Hide();
        };

        bool isDialogActive = true;
        while (isDialogActive)
        {
            shouldFetchCover = false;
            shouldRepairPath = false;
            var result = await dialog.ShowAsync();

            if (shouldFetchCover)
            {
                var tempCover = await FetchOrUploadCoverAsync(titleBox.Text, config.SteamGridDbApiKey);
                if (!string.IsNullOrEmpty(tempCover))
                {
                    var finalPath = Path.Combine(_dbService.CoversDirectory, $"{game.Id}.jpg");
                    
                    if (!string.IsNullOrEmpty(game.CoverPath) && File.Exists(game.CoverPath))
                    {
                        try { File.Delete(game.CoverPath); } catch { }
                    }

                    try
                    {
                        if (File.Exists(tempCover))
                        {
                            File.Move(tempCover, finalPath, true);
                            game.CoverPath = null;
                            game.CoverPath = finalPath;
                            _dbService.UpdateGame(game);
                            fetchCoverBtn.Content = "Cover Art Updated ✓";
                        }
                    }
                    catch { }
                }
            }
            else if (shouldRepairPath)
            {
                await RepairPathAsync(game);
                folderText.Text = $"Folder: {game.Folder}";
                exeText.Text = $"Executable: {game.ExePath}";
            }
            else if (result == ContentDialogResult.Primary)
            {
                game.Title = titleBox.Text;
                game.Tags = tagsBox.Text;
                _dbService.UpdateGame(game);
                ApplyFilters();
                RefreshTagsFilterCombo();
                isDialogActive = false;
            }
            else
            {
                isDialogActive = false;
            }
        }
    }

    private async Task<string> FetchOrUploadCoverAsync(string searchTitle, string apiKey)
    {
        var coverService = new CoverArtService(apiKey);
        var candidates = await coverService.SearchGameAsync(searchTitle);
        
        if (candidates.Count == 0)
        {
            return await PickManualCoverAsync();
        }
        
        int selectedGameId = -1;
        if (candidates.Count == 1)
        {
            selectedGameId = candidates[0].Id;
        }
        else
        {
            selectedGameId = await ShowDisambiguationDialogAsync(candidates);
            if (selectedGameId == -1)
            {
                return null;
            }
            if (selectedGameId == -2)
            {
                return await PickManualCoverAsync();
            }
        }
        
        var grids = await coverService.GetGridUrlsAsync(selectedGameId);
        if (grids.Count == 0)
        {
            return await PickManualCoverAsync();
        }
        
        return await ShowGridPickerAsync(grids);
    }

    private async Task<string> PickManualCoverAsync()
    {
        var filePicker = new FileOpenPicker();
        filePicker.FileTypeFilter.Add(".png");
        filePicker.FileTypeFilter.Add(".jpg");
        filePicker.FileTypeFilter.Add(".jpeg");
        filePicker.FileTypeFilter.Add(".bmp");
        filePicker.FileTypeFilter.Add(".gif");
        filePicker.FileTypeFilter.Add(".webp");
        
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.StartupWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(filePicker, hwnd);
        
        var file = await filePicker.PickSingleFileAsync();
        if (file != null)
        {
            try
            {
                var tempName = $"temp_{Guid.NewGuid()}{Path.GetExtension(file.Path)}";
                var tempPath = Path.Combine(_dbService.CoversDirectory, tempName);
                File.Copy(file.Path, tempPath, true);
                return tempPath;
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    private async Task<int> ShowDisambiguationDialogAsync(List<(int Id, string Name, int? ReleaseYear)> candidates)
    {
        var dialog = new ContentDialog
        {
            Title = "Select Game",
            PrimaryButtonText = "Select",
            SecondaryButtonText = "Upload Manually",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false
        };

        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            Margin = new Thickness(0, 12, 0, 12)
        };

        var items = candidates.Select(c => $"{c.Name}{(c.ReleaseYear.HasValue ? $" ({c.ReleaseYear.Value})" : "")}").ToList();
        list.ItemsSource = items;

        list.SelectionChanged += (s, e) =>
        {
            dialog.IsPrimaryButtonEnabled = list.SelectedIndex >= 0;
        };

        dialog.Content = list;
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && list.SelectedIndex >= 0)
        {
            return candidates[list.SelectedIndex].Id;
        }
        if (result == ContentDialogResult.Secondary)
        {
            return -2;
        }
        return -1;
    }

    private async Task<string> ShowGridPickerAsync(List<GridImage> grids)
    {
        var dialog = new ContentDialog
        {
            Title = "Select Cover Art",
            SecondaryButtonText = "Upload Manually",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot
        };

        var mainStack = new StackPanel { Spacing = 16 };
        
        var gridStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        string selectedUrl = null;

        int count = Math.Min(grids.Count, 3);
        for (int i = 0; i < count; i++)
        {
            var gridItem = grids[i];
            var img = new Image
            {
                Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(gridItem.Thumb)),
                Width = 110,
                Height = 160,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill
            };

            var btn = new Button
            {
                Content = img,
                Padding = new Thickness(4),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
            };

            btn.Click += (s, e) =>
            {
                selectedUrl = gridItem.Url;
                dialog.Hide();
            };

            gridStack.Children.Add(btn);
        }

        mainStack.Children.Add(gridStack);
        dialog.Content = mainStack;

        var result = await dialog.ShowAsync();
        if (selectedUrl != null)
        {
            return await DownloadCoverToTempAsync(selectedUrl);
        }
        if (result == ContentDialogResult.Secondary)
        {
            return await PickManualCoverAsync();
        }
        return null;
    }

    private async Task<string> DownloadCoverToTempAsync(string imageUrl)
    {
        try
        {
            using var client = new HttpClient();
            var bytes = await client.GetByteArrayAsync(imageUrl);
            var tempName = $"temp_{Guid.NewGuid()}.jpg";
            var tempPath = Path.Combine(_dbService.CoversDirectory, tempName);
            await File.WriteAllBytesAsync(tempPath, bytes);
            return tempPath;
        }
        catch
        {
            return null;
        }
    }

    private async void ChangeCover_MenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is Game game)
        {
            var filePicker = new FileOpenPicker();
            filePicker.FileTypeFilter.Add(".png");
            filePicker.FileTypeFilter.Add(".jpg");
            filePicker.FileTypeFilter.Add(".jpeg");
            filePicker.FileTypeFilter.Add(".bmp");
            filePicker.FileTypeFilter.Add(".gif");
            filePicker.FileTypeFilter.Add(".webp");
            
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.StartupWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(filePicker, hwnd);
            
            var file = await filePicker.PickSingleFileAsync();
            if (file != null)
            {
                await UpdateGameCoverAsync(game, file.Path);
            }
        }
    }

    private async void ReDetectExe_MenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is Game game)
        {
            await ReDetectExeAsync(game);
        }
    }

    private async Task ReDetectExeAsync(Game game)
    {
        var candidates = GetExeCandidates(game.Folder);
        
        if (candidates.Count == 0)
        {
            var errorDialog = new ContentDialog
            {
                Title = "No Executables Found",
                Content = $"We could not find any executables in the folder:\n{game.Folder}\n\nPlease verify the directory contains the game or use 'Repair Path' to reconfigure.",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await errorDialog.ShowAsync();
            return;
        }
        
        if (candidates.Count == 1)
        {
            var candidatePath = candidates[0];
            var candidateName = Path.GetFileName(candidatePath);
            var relativePath = Path.GetRelativePath(game.Folder, candidatePath);
            
            var confirmDialog = new ContentDialog
            {
                Title = "Confirm Executable Re-detection",
                Content = $"We detected exactly one candidate executable:\n\n{candidateName}\n\nWould you like to update the game executable path to this?",
                PrimaryButtonText = "Yes",
                CloseButtonText = "No",
                XamlRoot = this.XamlRoot
            };
            
            var result = await confirmDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                game.ExePath = relativePath;
                _dbService.UpdateGame(game);
            }
            return;
        }
        
        var pickerDialog = new ContentDialog
        {
            Title = "Select Executable",
            PrimaryButtonText = "Confirm",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };
        
        var stack = new StackPanel { Spacing = 10 };
        var textBlock = new TextBlock { Text = "Multiple executables were found. Please select the correct one:", TextWrapping = TextWrapping.Wrap };
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        
        var relativePaths = candidates.Select(c => Path.GetRelativePath(game.Folder, c)).ToList();
        combo.ItemsSource = relativePaths;
        
        var previousIndex = relativePaths.FindIndex(r => string.Equals(r, game.ExePath, StringComparison.OrdinalIgnoreCase));
        if (previousIndex >= 0)
        {
            combo.SelectedIndex = previousIndex;
        }
        else
        {
            combo.SelectedIndex = 0;
        }
        
        stack.Children.Add(textBlock);
        stack.Children.Add(combo);
        pickerDialog.Content = stack;
        
        var res = await pickerDialog.ShowAsync();
        if (res == ContentDialogResult.Primary && combo.SelectedItem is string selectedRelativePath)
        {
            game.ExePath = selectedRelativePath;
            _dbService.UpdateGame(game);
        }
    }

    private async void RepairPath_MenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is Game game)
        {
            await RepairPathAsync(game);
        }
    }

    private async Task RepairPathAsync(Game game)
    {
        var folderPicker = new FolderPicker();
        folderPicker.FileTypeFilter.Add("*");
        
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.StartupWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
        
        var folder = await folderPicker.PickSingleFolderAsync();
        if (folder == null) return;
        
        var newFolderPath = folder.Path;
        var candidates = GetExeCandidates(newFolderPath);
        
        if (candidates.Count == 0)
        {
            var errorDialog = new ContentDialog
            {
                Title = "No Executables Found",
                Content = $"We could not find any executables in the selected folder:\n{newFolderPath}\n\nPlease verify and try again.",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await errorDialog.ShowAsync();
            return;
        }
        
        string selectedRelativeExe = "";
        
        if (candidates.Count == 1)
        {
            var candidatePath = candidates[0];
            var candidateName = Path.GetFileName(candidatePath);
            var relativePath = Path.GetRelativePath(newFolderPath, candidatePath);
            
            var confirmDialog = new ContentDialog
            {
                Title = "Confirm Executable",
                Content = $"We detected exactly one candidate executable in the new folder:\n\n{candidateName}\n\nWould you like to select this as the game executable?",
                PrimaryButtonText = "Yes",
                CloseButtonText = "No",
                XamlRoot = this.XamlRoot
            };
            
            var result = await confirmDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                selectedRelativeExe = relativePath;
            }
            else
            {
                return;
            }
        }
        else
        {
            var pickerDialog = new ContentDialog
            {
                Title = "Select Executable",
                PrimaryButtonText = "Confirm",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };
            
            var stack = new StackPanel { Spacing = 10 };
            var textBlock = new TextBlock { Text = "Multiple executables were found in the folder. Please select the correct one:", TextWrapping = TextWrapping.Wrap };
            var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            
            var relativePaths = candidates.Select(c => Path.GetRelativePath(newFolderPath, c)).ToList();
            combo.ItemsSource = relativePaths;
            
            var oldExeName = Path.GetFileName(game.ExePath);
            var matchIndex = relativePaths.FindIndex(r => string.Equals(Path.GetFileName(r), oldExeName, StringComparison.OrdinalIgnoreCase));
            if (matchIndex >= 0)
            {
                combo.SelectedIndex = matchIndex;
            }
            else
            {
                combo.SelectedIndex = 0;
            }
            
            stack.Children.Add(textBlock);
            stack.Children.Add(combo);
            pickerDialog.Content = stack;
            
            var res = await pickerDialog.ShowAsync();
            if (res == ContentDialogResult.Primary && combo.SelectedItem is string selectedVal)
            {
                selectedRelativeExe = selectedVal;
            }
            else
            {
                return;
            }
        }
        
        game.Folder = newFolderPath;
        game.ExePath = selectedRelativeExe;
        _dbService.UpdateGame(game);
    }

    private async void RemoveGame_MenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is Game game)
        {
            await RemoveGameAsync(game);
        }
    }

    private async Task RemoveGameAsync(Game game)
    {
        var confirmDialog = new ContentDialog
        {
            Title = "Remove Game",
            Content = $"Are you sure you want to remove '{game.Title}' from your shelf?\nThis will not delete the files on disk, only the database entry and saved cover art.",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        var result = await confirmDialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            _dbService.DeleteGame(game.Id);
            if (!string.IsNullOrEmpty(game.CoverPath) && File.Exists(game.CoverPath))
            {
                try { File.Delete(game.CoverPath); } catch { }
            }
            AllGames.Remove(game);
            ApplyFilters();
            RefreshTagsFilterCombo();
        }
    }

    private void Card_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Drop to change cover art";
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsContentVisible = true;
    }

    private async void Card_Drop(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            if (items.Count > 0 && items[0] is Windows.Storage.StorageFile file)
            {
                var ext = Path.GetExtension(file.Path).ToLower();
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif" || ext == ".webp")
                {
                    var grid = sender as Grid;
                    var game = grid?.Tag as Game;
                    if (game != null)
                    {
                        await UpdateGameCoverAsync(game, file.Path);
                    }
                }
            }
        }
    }

    private async Task UpdateGameCoverAsync(Game game, string sourceFilePath)
    {
        try
        {
            var destPath = Path.Combine(_dbService.CoversDirectory, $"{game.Id}.jpg");
            
            if (!string.Equals(sourceFilePath, destPath, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(destPath))
                {
                    try { File.Delete(destPath); } catch { }
                }
                
                if (!string.IsNullOrEmpty(game.CoverPath) && File.Exists(game.CoverPath) && !string.Equals(game.CoverPath, destPath, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(game.CoverPath); } catch { }
                }

                File.Copy(sourceFilePath, destPath, true);
            }
            
            game.CoverPath = null;
            game.CoverPath = destPath;
            _dbService.UpdateGame(game);
        }
        catch (Exception ex)
        {
            var errorDialog = new ContentDialog
            {
                Title = "Error Saving Cover Art",
                Content = $"An error occurred while copying the cover art:\n{ex.Message}",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await errorDialog.ShowAsync();
        }
    }

    private List<string> GetExeCandidates(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return new List<string>();
        
        try
        {
            var exes = Directory.GetFiles(folderPath, "*.exe", SearchOption.TopDirectoryOnly);
            var filtered = new List<string>();
            foreach (var file in exes)
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith("unins", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("redist", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("vcredist", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("dxsetup", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("crashreporter", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("unitycrashhandler", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                filtered.Add(file);
            }
            return filtered;
        }
        catch
        {
            return new List<string>();
        }
    }

    private string CleanFolderTitle(string folderPath)
    {
        var title = Path.GetFileName(folderPath) ?? "";
        
        string[] suffixes = {
            " - Gold Edition", " - Definitive Edition", " - Remastered", " - Game of the Year Edition", " - Game of the Year",
            " - GOTY", " - Deluxe Edition", " - Special Edition", " - Ultimate Edition", " - Director's Cut", " - Directors Cut",
            " - Enhanced Edition", " - Anniversary Edition", " - Limited Edition",
            "- Gold Edition", "- Definitive Edition", "- Remastered", "- Game of the Year Edition", "- Game of the Year",
            "- Deluxe Edition", "- Special Edition", "- Ultimate Edition", "- Director's Cut", "- Enhanced Edition",
            "- Anniversary Edition", "- Limited Edition"
        };

        foreach (var suffix in suffixes)
        {
            if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                title = title.Substring(0, title.Length - suffix.Length);
                break;
            }
        }

        title = title.Replace('_', ' ').Replace('-', ' ');
        title = System.Text.RegularExpressions.Regex.Replace(title, @"\s+", " ");
        return title.Trim();
    }
}
