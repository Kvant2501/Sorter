using PhotoSorterApp.Models;
using PhotoSorterApp.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PhotoSorterApp.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    public ObservableCollection<FileTypeProfile> Profiles { get; } = new()
{
    FileTypeProfile.PhotosOnly,
    FileTypeProfile.VideosOnly,
    FileTypeProfile.PhotosAndVideos,
    FileTypeProfile.AllSupported
};
    private FileTypeProfile _selectedProfile = FileTypeProfile.PhotosOnly;
    public FileTypeProfile SelectedProfile
    {
        get => _selectedProfile;
        set { _selectedProfile = value; OnPropertyChanged(); }
    }

    private SortingOptions _sortingOptions = new();
    public SortingOptions SortingOptions
    {
        get => _sortingOptions;
        set { _sortingOptions = value; OnPropertyChanged(); }
    }

    private string _duplicatesSearchFolder = "";
    public string DuplicatesSearchFolder
    {
        get => _duplicatesSearchFolder;
        set { _duplicatesSearchFolder = value; OnPropertyChanged(); }
    }

    private bool _isDuplicatesRecursive = true;
    public bool IsDuplicatesRecursive
    {
        get => _isDuplicatesRecursive;
        set { _isDuplicatesRecursive = value; OnPropertyChanged(); }
    }

    private string _cleanupFolder = "";
    public string CleanupFolder
    {
        get => _cleanupFolder;
        set { _cleanupFolder = value; OnPropertyChanged(); }
    }

    private bool _cleanupRecursive = true;
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

    private string _renameFolder = "";
    public string RenameFolder
    {
        get => _renameFolder;
        set { _renameFolder = value; OnPropertyChanged(); }
    }

    private string _renamePattern = "Фото_{date}_{index}";
    public string RenamePattern
    {
        get => _renamePattern;
        set { _renamePattern = value; OnPropertyChanged(); }
    }

    private bool _isRenameRecursive = false;
    public bool IsRenameRecursive
    {
        get => _isRenameRecursive;
        set { _isRenameRecursive = value; OnPropertyChanged(); }
    }

    private bool _isSortOnly = true;
    public bool IsSortOnly
    {
        get => _isSortOnly;
        set { _isSortOnly = value; OnPropertyChanged(); }
    }

    private bool _isSortAndDuplicates = false;
    public bool IsSortAndDuplicates
    {
        get => _isSortAndDuplicates;
        set { _isSortAndDuplicates = value; OnPropertyChanged(); }
    }

    private bool _isProgressVisible = false;
    public bool IsProgressVisible
    {
        get => _isProgressVisible;
        set { _isProgressVisible = value; OnPropertyChanged(); }
    }

    private int _progressValue = 0;
    public int ProgressValue
    {
        get => _progressValue;
        set { _progressValue = value; OnPropertyChanged(); }
    }

    public LogCollection Logger { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}