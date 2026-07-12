using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace GameShelf;

public class Game : INotifyPropertyChanged
{
    private int _id;
    private string _title = "";
    private string _folder = "";
    private string _exePath = "";
    private string? _coverPath;
    private long _playTimeSeconds;
    private string? _tags;
    private bool _isRunning;
    private string? _dateAdded;
    private string? _lastPlayedText;
    private string? _displayLastPlayedText;
    private string? _displayRecentPlayTime;
    private bool _isNewToLibrary;
    private Windows.UI.Color _dominantColor = Windows.UI.Color.FromArgb(255, 30, 32, 40);
    private string? _accentColorPrimary;
    private string? _accentColorSecondary;

    public int Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public string Folder
    {
        get => _folder;
        set => SetField(ref _folder, value);
    }

    public string ExePath
    {
        get => _exePath;
        set => SetField(ref _exePath, value);
    }

    public string? CoverPath
    {
        get => _coverPath;
        set
        {
            if (SetField(ref _coverPath, value))
            {
                OnPropertyChanged(nameof(HasCover));
                OnPropertyChanged(nameof(CoverImageSource));
            }
        }
    }

    public long PlayTimeSeconds
    {
        get => _playTimeSeconds;
        set
        {
            if (SetField(ref _playTimeSeconds, value))
            {
                OnPropertyChanged(nameof(DisplayPlayTime));
            }
        }
    }

    public string? Tags
    {
        get => _tags;
        set
        {
            if (SetField(ref _tags, value))
            {
                OnPropertyChanged(nameof(TagList));
            }
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (SetField(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(RunningVisibility));
            }
        }
    }

    public string StatusText => IsRunning ? "Running" : "";

    public Microsoft.UI.Xaml.Visibility RunningVisibility => IsRunning ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public string? DateAdded
    {
        get => _dateAdded;
        set => SetField(ref _dateAdded, value);
    }

    public string? LastPlayedText
    {
        get => _lastPlayedText;
        set
        {
            if (SetField(ref _lastPlayedText, value))
            {
                OnPropertyChanged(nameof(LastPlayedVisibility));
            }
        }
    }

    public string? DisplayLastPlayedText
    {
        get => _displayLastPlayedText;
        set => SetField(ref _displayLastPlayedText, value);
    }

    public string? DisplayRecentPlayTime
    {
        get => _displayRecentPlayTime;
        set => SetField(ref _displayRecentPlayTime, value);
    }

    public bool IsNewToLibrary
    {
        get => _isNewToLibrary;
        set
        {
            if (SetField(ref _isNewToLibrary, value))
            {
                OnPropertyChanged(nameof(NewToLibraryVisibility));
            }
        }
    }

    public Windows.UI.Color DominantColor
    {
        get => _dominantColor;
        set
        {
            if (SetField(ref _dominantColor, value))
            {
                OnPropertyChanged(nameof(AmbientGlowColor));
            }
        }
    }

    public Microsoft.UI.Xaml.Visibility NewToLibraryVisibility => IsNewToLibrary ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility LastPlayedVisibility => !string.IsNullOrEmpty(LastPlayedText) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Windows.UI.Color AmbientGlowColor
    {
        get
        {
            // Boost dominant color brightness for a vibrant halo glow
            double factor = 1.8;
            byte r = (byte)Math.Clamp(DominantColor.R * factor, 0, 255);
            byte g = (byte)Math.Clamp(DominantColor.G * factor, 0, 255);
            byte b = (byte)Math.Clamp(DominantColor.B * factor, 0, 255);
            return Windows.UI.Color.FromArgb(220, r, g, b); // Bright and beautiful opacity
        }
    }

    public string? AccentColorPrimary
    {
        get => _accentColorPrimary;
        set
        {
            if (SetField(ref _accentColorPrimary, value))
            {
                OnPropertyChanged(nameof(AmbientGlowColorPrimary));
            }
        }
    }

    public string? AccentColorSecondary
    {
        get => _accentColorSecondary;
        set
        {
            if (SetField(ref _accentColorSecondary, value))
            {
                OnPropertyChanged(nameof(AmbientGlowColorSecondary));
            }
        }
    }

    public Windows.UI.Color AmbientGlowColorPrimary => ColorFromHex(AccentColorPrimary ?? "#FF1b2838");
    public Windows.UI.Color AmbientGlowColorSecondary => ColorFromHex(AccentColorSecondary ?? "#FF1e222a");

    public static string ColorToHex(Windows.UI.Color color)
    {
        return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    public static Windows.UI.Color ColorFromHex(string hex)
    {
        try
        {
            if (string.IsNullOrEmpty(hex)) return Windows.UI.Color.FromArgb(255, 27, 40, 56);
            hex = hex.Replace("#", "");
            byte a = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
            byte r = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
            byte g = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
            byte b = byte.Parse(hex.Substring(6, 2), System.Globalization.NumberStyles.HexNumber);
            return Windows.UI.Color.FromArgb(a, r, g, b);
        }
        catch
        {
            return Windows.UI.Color.FromArgb(255, 27, 40, 56);
        }
    }

    public string DisplayPlayTime => FormatPlayTime(PlayTimeSeconds);

    public bool HasCover => !string.IsNullOrEmpty(CoverPath) && File.Exists(CoverPath);

    public string CoverImageSource => HasCover ? CoverPath! : "ms-appx:///Assets/StoreLogo.png";

    public List<string> TagList => string.IsNullOrEmpty(Tags)
        ? new List<string>()
        : Tags.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList();

    public static string FormatPlayTime(long seconds)
    {
        if (seconds == 0) return "0m";
        var span = TimeSpan.FromSeconds(seconds);
        if (seconds < 60)
        {
            return $"{seconds}s";
        }
        if (span.TotalHours >= 1)
        {
            return $"{span.TotalHours:F1}h";
        }
        return $"{span.TotalMinutes:F0}m";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
