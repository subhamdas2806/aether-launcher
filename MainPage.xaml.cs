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
using Microsoft.UI.Xaml.Media.Animation;
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

        Loaded += (s, e) => LoadGames();
    }

    private static async Task<(string PrimaryHex, string SecondaryHex)> ExtractTwoToneColorsAsync(string imagePath)
    {
        try
        {
            if (!File.Exists(imagePath)) return ("#FF1b2838", "#FF1e222a");
            
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(imagePath);
            using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read);
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
            
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();
            var buffer = new Windows.Storage.Streams.Buffer((uint)(softwareBitmap.PixelWidth * softwareBitmap.PixelHeight * 4));
            softwareBitmap.CopyToBuffer(buffer);
            var reader = Windows.Storage.Streams.DataReader.FromBuffer(buffer);
            var bytes = new byte[buffer.Capacity];
            reader.ReadBytes(bytes);
            
            long rSum = 0, gSum = 0, bSum = 0;
            int totalCount = 0;
            var sampleColors = new List<Windows.UI.Color>();
            
            for (int i = 0; i < bytes.Length; i += 400)
            {
                if (i + 2 < bytes.Length)
                {
                    byte bVal = bytes[i];
                    byte gVal = bytes[i + 1];
                    byte rVal = bytes[i + 2];
                    
                    rSum += rVal;
                    gSum += gVal;
                    bSum += bVal;
                    totalCount++;
                    
                    sampleColors.Add(Windows.UI.Color.FromArgb(255, rVal, gVal, bVal));
                }
            }
            
            if (totalCount == 0) return ("#FF1b2838", "#FF1e222a");
            
            double avgR = (double)rSum / totalCount;
            double avgG = (double)gSum / totalCount;
            double avgB = (double)bSum / totalCount;
            double avgLuminance = 0.299 * avgR + 0.587 * avgG + 0.114 * avgB;
            
            long lightR = 0, lightG = 0, lightB = 0, lightCount = 0;
            long darkR = 0, darkG = 0, darkB = 0, darkCount = 0;
            
            foreach (var color in sampleColors)
            {
                double lum = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
                if (lum >= avgLuminance)
                {
                    lightR += color.R;
                    lightG += color.G;
                    lightB += color.B;
                    lightCount++;
                }
                else
                {
                    darkR += color.R;
                    darkG += color.G;
                    darkB += color.B;
                    darkCount++;
                }
            }
            
            Windows.UI.Color primaryColor = Windows.UI.Color.FromArgb(255, (byte)avgR, (byte)avgG, (byte)avgB);
            Windows.UI.Color secondaryColor = Windows.UI.Color.FromArgb(255, (byte)avgR, (byte)avgG, (byte)avgB);
            
            if (lightCount > 0)
            {
                primaryColor = Windows.UI.Color.FromArgb(255, (byte)(lightR / lightCount), (byte)(lightG / lightCount), (byte)(lightB / lightCount));
            }
            if (darkCount > 0)
            {
                secondaryColor = Windows.UI.Color.FromArgb(255, (byte)(darkR / darkCount), (byte)(darkG / darkCount), (byte)(darkB / darkCount));
            }
            
            var primaryClamped = ClampColorForDarkTheme(primaryColor);
            var secondaryClamped = ClampColorForDarkTheme(secondaryColor);
            
            return (Game.ColorToHex(primaryClamped), Game.ColorToHex(secondaryClamped));
        }
        catch { }
        return ("#FF1b2838", "#FF1e222a");
    }

    public static Windows.UI.Color ClampColorForDarkTheme(Windows.UI.Color color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double h = 0, s = 0, l = (max + min) / 2.0;

        if (max != min)
        {
            double d = max - min;
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
            if (max == r)
                h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g)
                h = (b - r) / d + 2;
            else if (max == b)
                h = (r - g) / d + 4;
            h /= 6.0;
        }

        if (s < 0.15) s = 0.25; 
        if (l > 0.45) l = 0.45; 
        if (l < 0.15) l = 0.15; 

        double q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
        double p = 2.0 * l - q;

        double ToRgb(double tc)
        {
            if (tc < 0) tc += 1.0;
            if (tc > 1) tc -= 1.0;
            if (tc < 1.0 / 6.0) return p + (q - p) * 6.0 * tc;
            if (tc < 1.0 / 2.0) return q;
            if (tc < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - tc) * 6.0;
            return p;
        }

        byte rOut = (byte)Math.Clamp(ToRgb(h + 1.0 / 3.0) * 255.0, 0, 255);
        byte gOut = (byte)Math.Clamp(ToRgb(h) * 255.0, 0, 255);
        byte bOut = (byte)Math.Clamp(ToRgb(h - 1.0 / 3.0) * 255.0, 0, 255);

        return Windows.UI.Color.FromArgb(255, rOut, gOut, bOut);
    }

    private void LoadGames()
    {
        AllGames.Clear();
        var games = _dbService.GetAllGames();
        var dispatcher = this.DispatcherQueue;
        foreach (var game in games)
        {
            var lastPlayed = _dbService.GetLastPlayed(game.Id);
            if (lastPlayed.HasValue)
            {
                game.LastPlayedText = lastPlayed.Value.ToString("MMM d, yyyy");
                game.DisplayLastPlayedText = $"Last played: {game.LastPlayedText}";
            }
            else
            {
                game.LastPlayedText = null;
                game.DisplayLastPlayedText = "Never played";
            }

            var recentSeconds = _dbService.GetRecentPlayTimeSeconds(game.Id);
            game.DisplayRecentPlayTime = Game.FormatPlayTime(recentSeconds);

            game.IsNewToLibrary = game.PlayTimeSeconds == 0 || 
                                  (game.DateAdded != null && DateTime.TryParse(game.DateAdded, out var dt) && (DateTime.UtcNow - dt).TotalDays <= 7);

            if (game.HasCover)
            {
                var coverPath = game.CoverPath!;
                var targetGame = game;
                if (string.IsNullOrEmpty(game.AccentColorPrimary) || string.IsNullOrEmpty(game.AccentColorSecondary))
                {
                    Task.Run(async () =>
                    {
                        var (primary, secondary) = await ExtractTwoToneColorsAsync(coverPath);
                        _dbService.UpdateGameColors(targetGame.Id, primary, secondary);
                        dispatcher.TryEnqueue(() =>
                        {
                            targetGame.AccentColorPrimary = primary;
                            targetGame.AccentColorSecondary = secondary;
                            targetGame.DominantColor = targetGame.AmbientGlowColorPrimary;
                        });
                    });
                }
                else
                {
                    dispatcher.TryEnqueue(() =>
                    {
                        targetGame.DominantColor = targetGame.AmbientGlowColorPrimary;
                    });
                }
            }

            AllGames.Add(game);
        }
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var sidebarSearchText = SidebarSearchBox != null ? SidebarSearchBox.Text.Trim() : "";

        var filtered = AllGames.AsEnumerable();

        if (!string.IsNullOrEmpty(sidebarSearchText))
        {
            filtered = filtered.Where(g => 
                g.Title.Contains(sidebarSearchText, StringComparison.OrdinalIgnoreCase) || 
                (g.Tags != null && g.Tags.Contains(sidebarSearchText, StringComparison.OrdinalIgnoreCase))
            );
        }

        FilteredGames.Clear();
        foreach (var game in filtered)
        {
            FilteredGames.Add(game);
        }
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
                            
                            var (primary, secondary) = await ExtractTwoToneColorsAsync(finalPath);
                            newGame.AccentColorPrimary = primary;
                            newGame.AccentColorSecondary = secondary;
                            newGame.DominantColor = newGame.AmbientGlowColorPrimary;
                            
                            _dbService.UpdateGame(newGame);
                        }
                    }
                    catch { }
                }

                AllGames.Add(newGame);
                ApplyFilters();
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

    private struct CardElements
    {
        public PlaneProjection? Projection;
        public ScaleTransform? Scale;
        public TranslateTransform? Translate;
        public Border? AmbientGlow;
        public Border? CardBorder;
        public SolidColorBrush? BorderBrush;
        public Border? HolographicShimmer;
        public LinearGradientBrush? ShimmerBrush;
        public Microsoft.UI.Xaml.Controls.Primitives.Popup? HoverPopup;
    }

    private CardElements GetCardElements(Grid cardRoot)
    {
        var elements = new CardElements();
        
        elements.Projection = cardRoot.Projection as PlaneProjection;
        
        if (cardRoot.RenderTransform is TransformGroup tg)
        {
            if (tg.Children.Count > 0) elements.Scale = tg.Children[0] as ScaleTransform;
            if (tg.Children.Count > 1) elements.Translate = tg.Children[1] as TranslateTransform;
        }
        
        if (cardRoot.Children.Count > 0 && cardRoot.Children[0] is Border glow)
        {
            elements.AmbientGlow = glow;
        }
        
        if (cardRoot.Children.Count > 1 && cardRoot.Children[1] is Border cardBorder)
        {
            elements.CardBorder = cardBorder;
            elements.BorderBrush = cardBorder.BorderBrush as SolidColorBrush;
            
            if (cardBorder.Child is Grid cardGrid && cardGrid.Children.Count > 1 && cardGrid.Children[1] is Border shimmer)
            {
                elements.HolographicShimmer = shimmer;
                elements.ShimmerBrush = shimmer.Background as LinearGradientBrush;
            }
        }
        
        if (cardRoot.Children.Count > 2 && cardRoot.Children[2] is Microsoft.UI.Xaml.Controls.Primitives.Popup popup)
        {
            elements.HoverPopup = popup;
        }
        
        return elements;
    }

    private void AnimateDouble(DependencyObject target, string path, double? from, double to, double durationMs)
    {
        if (target == null) return;
        var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut },
            EnableDependentAnimation = true
        };
        if (from.HasValue) animation.From = from.Value;
        
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, target);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, path);
        
        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private void AnimateColor(SolidColorBrush brush, Windows.UI.Color toColor, double durationMs)
    {
        if (brush == null) return;
        var animation = new Microsoft.UI.Xaml.Media.Animation.ColorAnimation
        {
            To = toColor,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
        };
        
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, brush);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "Color");
        
        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private void AnimateGlowColor(GradientStop targetStop, Windows.UI.Color toColor, double durationMs)
    {
        if (targetStop == null) return;
        var animation = new Microsoft.UI.Xaml.Media.Animation.ColorAnimation
        {
            To = toColor,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
        };
        
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, targetStop);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "Color");
        
        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }
    private bool _isSidebarOpen = false;

    private void HamburgerButton_Click(object sender, RoutedEventArgs e)
    {
        if (SidebarContainer == null) return;
        
        if (_isSidebarOpen)
        {
            AnimateDouble(SidebarContainer, "Width", null, 0, 250);
            _isSidebarOpen = false;
        }
        else
        {
            AnimateDouble(SidebarContainer, "Width", null, 300, 300);
            _isSidebarOpen = true;
        }
    }
    private void Card_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid cardRoot)
        {
            var el = GetCardElements(cardRoot);
            
            // 1. Focus Hovered Card (Scale & Lift)
            if (el.Scale != null)
            {
                AnimateDouble(el.Scale, "ScaleX", null, 1.12, 200);
                AnimateDouble(el.Scale, "ScaleY", null, 1.12, 200);
            }
            if (el.Translate != null)
            {
                AnimateDouble(el.Translate, "Y", null, -6, 200);
            }
            if (el.Projection != null)
            {
                AnimateDouble(el.Projection, "LocalOffsetZ", null, 25, 200);
            }
            if (el.BorderBrush != null)
            {
                AnimateColor(el.BorderBrush, Windows.UI.Color.FromArgb(80, 255, 255, 255), 200);
            }
            
            // 2. Set Canvas.ZIndex on parent GridViewItem so it draws on top of neighbor cards and dim overlay
            var container = GamesGrid.ContainerFromItem(cardRoot.Tag) as GridViewItem;
            if (container != null)
            {
                Canvas.SetZIndex(container, 10);
            }
            
            // 3. Bloom Local Color-Matched Blurred Halo Glow
            if (el.AmbientGlow != null)
            {
                AnimateDouble(el.AmbientGlow, "Opacity", null, 0.75, 200);
            }
            
            // 4. Fade in Holographic Iridescent Shimmer
            if (el.HolographicShimmer != null)
            {
                AnimateDouble(el.HolographicShimmer, "Opacity", null, 0.45, 200);
            }
            
            // 5. Crossfade Shared Glow Background (Catch-up lag)
            var game = cardRoot.Tag as Game;
            if (game != null && GlowStopPrimary != null && GlowStopSecondary != null && SharedAmbientGlow != null)
            {
                AnimateGlowColor(GlowStopPrimary, game.AmbientGlowColorPrimary, 400);
                AnimateGlowColor(GlowStopSecondary, game.AmbientGlowColorSecondary, 400);
                AnimateDouble(SharedAmbientGlow, "Opacity", null, 0.35, 400);
            }
            
            // 6. Full-Screen Dim Overlay Fading In
            if (ScreenDimOverlay != null)
            {
                AnimateDouble(ScreenDimOverlay, "Opacity", null, 0.85, 250);
            }
            
            // 7. Align details popup
            if (el.HoverPopup != null)
            {
                var transform = cardRoot.TransformToVisual(this);
                var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                
                double popupWidth = 238; 
                double cardWidth = cardRoot.ActualWidth;
                double screenWidth = this.ActualWidth;
                
                double leftOffset = cardWidth + 12;
                if (point.X + cardWidth + popupWidth > screenWidth)
                {
                    leftOffset = -popupWidth - 12;
                }
                
                el.HoverPopup.HorizontalOffset = leftOffset;
                el.HoverPopup.VerticalOffset = 16;
                el.HoverPopup.IsOpen = true;
            }
        }
    }

    private void Card_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid cardRoot)
        {
            var el = GetCardElements(cardRoot);
            if (el.Projection == null) return;
            
            var pos = e.GetCurrentPoint(cardRoot).Position;
            double width = cardRoot.ActualWidth;
            double height = cardRoot.ActualHeight;
            
            if (width <= 0 || height <= 0) return;
            
            // Max tilt 8deg (identical to React component physics)
            double rotateX = ((pos.Y - height / 2.0) / (height / 2.0)) * -8.0;
            double rotateY = ((pos.X - width / 2.0) / (width / 2.0)) * 8.0;
            
            el.Projection.RotationX = rotateX;
            el.Projection.RotationY = rotateY;
            
            // Holographic shimmer linear gradient shift (enchanted Pokemon card shine)
            if (el.HolographicShimmer != null && el.ShimmerBrush != null)
            {
                double xPercent = pos.X / width;
                double yPercent = pos.Y / height;
                
                el.ShimmerBrush.StartPoint = new Windows.Foundation.Point(xPercent - 0.5, yPercent - 0.5);
                el.ShimmerBrush.EndPoint = new Windows.Foundation.Point(xPercent + 0.5, yPercent + 0.5);
                
                el.HolographicShimmer.Opacity = 0.45;
            }
        }
    }

    private void Card_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid cardRoot)
        {
            var el = GetCardElements(cardRoot);
            
            // 1. Restore focused card
            if (el.Scale != null)
            {
                AnimateDouble(el.Scale, "ScaleX", null, 1.0, 150);
                AnimateDouble(el.Scale, "ScaleY", null, 1.0, 150);
            }
            if (el.Translate != null)
            {
                AnimateDouble(el.Translate, "Y", null, 0, 150);
            }
            if (el.Projection != null)
            {
                AnimateDouble(el.Projection, "RotationX", null, 0, 150);
                AnimateDouble(el.Projection, "RotationY", null, 0, 150);
                AnimateDouble(el.Projection, "LocalOffsetZ", null, 0, 150);
            }
            if (el.BorderBrush != null)
            {
                AnimateColor(el.BorderBrush, Windows.UI.Color.FromArgb(34, 255, 255, 255), 150);
            }
            
            // 2. Reset Canvas.ZIndex on parent container
            var container = GamesGrid.ContainerFromItem(cardRoot.Tag) as GridViewItem;
            if (container != null)
            {
                Canvas.SetZIndex(container, 0);
            }
            
            // 3. Fade out Local Ambient Haze Glow
            if (el.AmbientGlow != null)
            {
                AnimateDouble(el.AmbientGlow, "Opacity", null, 0.0, 150);
            }
            
            // 4. Fade out Holographic Iridescent Shimmer
            if (el.HolographicShimmer != null)
            {
                AnimateDouble(el.HolographicShimmer, "Opacity", null, 0.0, 150);
            }
            
            // 5. Restore shared background glow
            if (GlowStopPrimary != null && GlowStopSecondary != null && SharedAmbientGlow != null)
            {
                AnimateGlowColor(GlowStopPrimary, Windows.UI.Color.FromArgb(255, 27, 40, 56), 400); 
                AnimateGlowColor(GlowStopSecondary, Windows.UI.Color.FromArgb(255, 30, 34, 42), 400); 
                AnimateDouble(SharedAmbientGlow, "Opacity", null, 0.15, 400);
            }
            
            // 6. Fade out Full-Screen Dim Overlay
            if (ScreenDimOverlay != null)
            {
                AnimateDouble(ScreenDimOverlay, "Opacity", null, 0.0, 200);
            }
            
            if (el.HoverPopup != null)
            {
                el.HoverPopup.IsOpen = false;
            }
        }
    }

    private void Card_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is Grid cardRoot)
        {
            var el = GetCardElements(cardRoot);
            if (el.HoverPopup != null)
            {
                el.HoverPopup.IsOpen = false;
            }
        }
    }



    private void SidebarSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilters();
    }

    private void SidebarListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Game game)
        {
            GamesGrid?.ScrollIntoView(game);
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
            
            var (primary, secondary) = await ExtractTwoToneColorsAsync(destPath);
            game.AccentColorPrimary = primary;
            game.AccentColorSecondary = secondary;
            game.DominantColor = game.AmbientGlowColorPrimary;
            
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
