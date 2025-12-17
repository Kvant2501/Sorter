#nullable enable

using PhotoSorterApp.Models;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace PhotoSorterApp.ViewModels;

/// <summary>
/// Main window ViewModel — holds UI state and options for operations.
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    // === Section: Sorting ===
    private SortingOptions _sortingOptions = new();
    public SortingOptions SortingOptions
    {
        get => _sortingOptions;
        set { _sortingOptions = value; OnPropertyChanged(); }
    }

    private bool _isSortOnly = true;
    public bool IsSortOnly
    {
        get => _isSortOnly;
        set { _isSortOnly = value; OnPropertyChanged(); }
    }

    private bool _isSortAndDuplicates;
    public bool IsSortAndDuplicates
    {
        get => _isSortAndDuplicates;
        set { _isSortAndDuplicates = value; OnPropertyChanged(); }
    }

    // === Section: Duplicates ===
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

    // === Section: Cleanup ===
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

    // === Section: Rename ===
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

    // === File profile ===
    private FileTypeProfile _selectedProfile = FileTypeProfile.PhotosOnly;
    public FileTypeProfile SelectedProfile
    {
        get => _selectedProfile;
        set { _selectedProfile = value; OnPropertyChanged(); }
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