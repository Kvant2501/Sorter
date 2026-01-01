#nullable enable

using PhotoSorterApp.Models;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace PhotoSorterApp.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    // === Section: Sorting (Media) ===
    private SortingOptions _sortingOptions = new();
    public SortingOptions SortingOptions
    {
        get => _sortingOptions;
        set { _sortingOptions = value; OnPropertyChanged(); }
    }

    // === Section: Sorting (Documents) ===
    private SortingOptions _documentsSortingOptions = new();
    public SortingOptions DocumentsSortingOptions
    {
        get => _documentsSortingOptions;
        set { _documentsSortingOptions = value; OnPropertyChanged(); }
    }

    // === Duplicates ===
    private string? _duplicatesSearchFolder;
    public string? DuplicatesSearchFolder
    {
        get => _duplicatesSearchFolder;
        set { _duplicatesSearchFolder = value; OnPropertyChanged(); }
    }

    private bool _isDuplicatesRecursive;
    public bool IsDuplicatesRecursive
    {
        get => _isDuplicatesRecursive;
        set { _isDuplicatesRecursive = value; OnPropertyChanged(); }
    }

    private FileTypeProfile _duplicatesProfile = FileTypeProfile.PhotosOnly;
    public FileTypeProfile DuplicatesProfile
    {
        get => _duplicatesProfile;
        set
        {
            _duplicatesProfile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDuplicatesPhotosOnly));
            OnPropertyChanged(nameof(IsDuplicatesVideosOnly));
            OnPropertyChanged(nameof(IsDuplicatesPhotosAndVideos));
            OnPropertyChanged(nameof(IsDuplicatesDocumentsOnly));
        }
    }

    public bool IsDuplicatesPhotosOnly
    {
        get => DuplicatesProfile == FileTypeProfile.PhotosOnly;
        set { if (value) DuplicatesProfile = FileTypeProfile.PhotosOnly; }
    }

    public bool IsDuplicatesVideosOnly
    {
        get => DuplicatesProfile == FileTypeProfile.VideosOnly;
        set { if (value) DuplicatesProfile = FileTypeProfile.VideosOnly; }
    }

    public bool IsDuplicatesPhotosAndVideos
    {
        get => DuplicatesProfile == FileTypeProfile.PhotosAndVideos;
        set { if (value) DuplicatesProfile = FileTypeProfile.PhotosAndVideos; }
    }

    public bool IsDuplicatesDocumentsOnly
    {
        get => DuplicatesProfile == FileTypeProfile.DocumentsOnly;
        set { if (value) DuplicatesProfile = FileTypeProfile.DocumentsOnly; }
    }

    // === Cleanup ===
    private string? _cleanupFolder;
    public string? CleanupFolder
    {
        get => _cleanupFolder;
        set { _cleanupFolder = value; OnPropertyChanged(); }
    }

    private bool _cleanupRecursive;
    public bool CleanupRecursive
    {
        get => _cleanupRecursive;
        set { _cleanupRecursive = value; OnPropertyChanged(); }
    }

    private bool _cleanupScreenshots = true;
    public bool CleanupScreenshots
    {
        get => _cleanupScreenshots;
        set { _cleanupScreenshots = value; OnPropertyChanged(); }
    }

    private bool _cleanupTempFiles = true;
    public bool CleanupTempFiles
    {
        get => _cleanupTempFiles;
        set { _cleanupTempFiles = value; OnPropertyChanged(); }
    }

    private bool _cleanupEmptyFiles = true;
    public bool CleanupEmptyFiles
    {
        get => _cleanupEmptyFiles;
        set { _cleanupEmptyFiles = value; OnPropertyChanged(); }
    }

    // === Rename ===
    private string? _renameFolder;
    public string? RenameFolder
    {
        get => _renameFolder;
        set { _renameFolder = value; OnPropertyChanged(); }
    }

    private string _renamePattern = "{date}_{name}";
    public string RenamePattern
    {
        get => _renamePattern;
        set { _renamePattern = value; OnPropertyChanged(); }
    }

    private bool _isRenameRecursive;
    public bool IsRenameRecursive
    {
        get => _isRenameRecursive;
        set { _isRenameRecursive = value; OnPropertyChanged(); }
    }

    // === File profile (Media sorting) ===
    private FileTypeProfile _selectedProfile = FileTypeProfile.PhotosOnly;
    public FileTypeProfile SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            _selectedProfile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPhotosOnly));
            OnPropertyChanged(nameof(IsVideosOnly));
            OnPropertyChanged(nameof(IsPhotosAndVideos));
        }
    }

    public bool IsPhotosOnly
    {
        get => SelectedProfile == FileTypeProfile.PhotosOnly;
        set { if (value) SelectedProfile = FileTypeProfile.PhotosOnly; }
    }

    public bool IsVideosOnly
    {
        get => SelectedProfile == FileTypeProfile.VideosOnly;
        set { if (value) SelectedProfile = FileTypeProfile.VideosOnly; }
    }

    public bool IsPhotosAndVideos
    {
        get => SelectedProfile == FileTypeProfile.PhotosAndVideos;
        set { if (value) SelectedProfile = FileTypeProfile.PhotosAndVideos; }
    }

    // === UI logging ===
    /// <summary>
    /// Collection of log entries bound to UI for history display.
    /// </summary>
    public LogCollection Logger { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged(string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Simple log collection with helper Add method.
/// </summary>
public class LogCollection : ObservableCollection<LogEntry>
{
    public void Log(string message, LogLevel level, string icon = "")
    {
        Add(new LogEntry(message, level, icon));
    }
}

/// <summary>
/// Log entry — contains message, level and icon/marker.
/// </summary>
public class LogEntry
{
    public string Message { get; }
    public LogLevel Level { get; }
    public string Icon { get; }
    public DateTime Timestamp { get; } = DateTime.Now;

    public LogEntry(string message, LogLevel level, string icon)
    {
        Message = message;
        Level = level;
        Icon = icon;
    }
}

public enum LogLevel
{
    Info,
    Warning,
    Error
}