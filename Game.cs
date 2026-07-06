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
