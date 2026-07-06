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

    private async void AddGame_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Add Game",
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
            XamlRoot = this.XamlRoot
        };

        var stack = new StackPanel { Spacing = 12, Width = 380 };

        var titleBox = new TextBox { Header = "Game Title" };
        var tagsBox = new TextBox { Header = "Tags (comma-separated)" };

        var folderLabel = new TextBlock { Text = "Game Folder:", FontWeight = FontWeights.SemiBold };
        var folderPathText = new TextBlock { Text = "No folder selected", Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray), TextWrapping = TextWrapping.Wrap };
        var selectFolderBtn = new Button { Content = "Select Folder..." };

        var exeLabel = new TextBlock { Text = "Select Executable:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) };
        var exeCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false };

        stack.Children.Add(titleBox);
        stack.Children.Add(tagsBox);
        stack.Children.Add(folderLabel);
        stack.Children.Add(folderPathText);
        stack.Children.Add(selectFolderBtn);
        stack.Children.Add(exeLabel);
        stack.Children.Add(exeCombo);

        dialog.Content = stack;

        string selectedFolder = "";
        string selectedRelativeExe = "";

        selectFolderBtn.Click += async (s, args) =>
        {
            var folderPicker = new FolderPicker();
            folderPicker.FileTypeFilter.Add("*");
            
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.StartupWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
            
            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                selectedFolder = folder.Path;
                folderPathText.Text = selectedFolder;
                folderPathText.Foreground = App.Current.Resources["TextControlForeground"] as Brush;

                if (string.IsNullOrEmpty(titleBox.Text))
                {
                    titleBox.Text = Path.GetFileName(selectedFolder);
                }

                var candidates = GetExeCandidates(selectedFolder);
                if (candidates.Count > 0)
                {
                    var relativePaths = candidates.Select(c => Path.GetRelativePath(selectedFolder, c)).ToList();
                    exeCombo.ItemsSource = relativePaths;
                    exeCombo.IsEnabled = true;
                    exeCombo.SelectedIndex = 0;
                    selectedRelativeExe = relativePaths[0];
                    dialog.IsPrimaryButtonEnabled = true;
                }
                else
                {
                    exeCombo.ItemsSource = null;
                    exeCombo.IsEnabled = false;
                    selectedRelativeExe = "";
                    folderPathText.Text = $"{selectedFolder} (No executables found!)";
                    folderPathText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
                    dialog.IsPrimaryButtonEnabled = false;
                }
            }
        };

        exeCombo.SelectionChanged += (s, args) =>
        {
            if (exeCombo.SelectedItem is string relativePath)
            {
                selectedRelativeExe = relativePath;
                dialog.IsPrimaryButtonEnabled = true;
            }
            else
            {
                dialog.IsPrimaryButtonEnabled = false;
            }
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrEmpty(selectedFolder) && !string.IsNullOrEmpty(selectedRelativeExe))
        {
            var newGame = new Game
            {
                Title = string.IsNullOrEmpty(titleBox.Text) ? Path.GetFileName(selectedFolder) : titleBox.Text,
                Folder = selectedFolder,
                ExePath = selectedRelativeExe,
                Tags = tagsBox.Text
            };

            _dbService.InsertGame(newGame);
            AllGames.Add(newGame);
            ApplyFilters();
            RefreshTagsFilterCombo();
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
        var tagsBox = new TextBox { Header = "Tags (comma-separated)", Text = game.Tags };

        var pathLabel = new TextBlock { Text = "Path Settings", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 0) };
        var folderText = new TextBlock { Text = $"Folder: {game.Folder}", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray) };
        var exeText = new TextBlock { Text = $"Executable: {game.ExePath}", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray) };

        var repairBtn = new Button { Content = "Repair Path..." };

        stack.Children.Add(titleBox);
        stack.Children.Add(tagsBox);
        stack.Children.Add(pathLabel);
        stack.Children.Add(folderText);
        stack.Children.Add(exeText);
        stack.Children.Add(repairBtn);

        dialog.Content = stack;

        repairBtn.Click += async (s, args) =>
        {
            dialog.Hide();
            await RepairPathAsync(game);
            folderText.Text = $"Folder: {game.Folder}";
            exeText.Text = $"Executable: {game.ExePath}";
            await dialog.ShowAsync();
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            game.Title = titleBox.Text;
            game.Tags = tagsBox.Text;
            _dbService.UpdateGame(game);
            ApplyFilters();
            RefreshTagsFilterCombo();
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
            var ext = Path.GetExtension(sourceFilePath);
            var newFileName = $"{game.Id}_{Guid.NewGuid()}{ext}";
            var destPath = Path.Combine(_dbService.CoversDirectory, newFileName);
            
            if (!string.IsNullOrEmpty(game.CoverPath) && File.Exists(game.CoverPath))
            {
                try { File.Delete(game.CoverPath); } catch { }
            }
            
            File.Copy(sourceFilePath, destPath, true);
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
}
