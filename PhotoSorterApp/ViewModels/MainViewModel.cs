#nullable disable

using PhotoSorterApp.Models;
using PhotoSorterApp.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace PhotoSorterApp.ViewModels;


public class MainViewModel : INotifyPropertyChanged
{
    // ========== Сортировка ==========
    private SortingOptions _sortingOptions = new();
    public SortingOptions SortingOptions
    {
        get => _sortingOptions;
        set { _sortingOptions = value; OnPropertyChanged(); }
    }

    // Тип файлов (радиокнопки)
    private bool _isPhotosOnly = true;
    public bool IsPhotosOnly
    {
        get => _isPhotosOnly;
        set
        {
            if (value)
            {
                _isPhotosOnly = true;
                _isVideosOnly = false;
                _isPhotosAndVideos = false;
                _isAllSupported = false;
                SelectedProfile = FileTypeProfile.PhotosOnly;
                OnPropertyChanged(nameof(IsVideosOnly));
                OnPropertyChanged(nameof(IsPhotosAndVideos));
                OnPropertyChanged(nameof(IsAllSupported));
            }
            OnPropertyChanged();
        }
    }

    private bool _isVideosOnly;
    public bool IsVideosOnly
    {
        get => _isVideosOnly;
        set
        {
            if (value)
            {
                _isPhotosOnly = false;
                _isVideosOnly = true;
                _isPhotosAndVideos = false;
                _isAllSupported = false;
                SelectedProfile = FileTypeProfile.VideosOnly;
                OnPropertyChanged(nameof(IsPhotosOnly));
                OnPropertyChanged(nameof(IsPhotosAndVideos));
                OnPropertyChanged(nameof(IsAllSupported));
            }
            OnPropertyChanged();
        }
    }

    private bool _isPhotosAndVideos;
    public bool IsPhotosAndVideos
    {
        get => _isPhotosAndVideos;
        set
        {
            if (value)
            {
                _isPhotosOnly = false;
                _isVideosOnly = false;
                _isPhotosAndVideos = true;
                _isAllSupported = false;
                SelectedProfile = FileTypeProfile.PhotosAndVideos;
                OnPropertyChanged(nameof(IsPhotosOnly));
                OnPropertyChanged(nameof(IsVideosOnly));
                OnPropertyChanged(nameof(IsAllSupported));
            }
            OnPropertyChanged();
        }
    }

    private bool _isAllSupported;
    public bool IsAllSupported
    {
        get => _isAllSupported;
        set
        {
            if (value)
            {
                _isPhotosOnly = false;
                _isVideosOnly = false;
                _isPhotosAndVideos = false;
                _isAllSupported = true;
                SelectedProfile = FileTypeProfile.AllSupported;
                OnPropertyChanged(nameof(IsPhotosOnly));
                OnPropertyChanged(nameof(IsVideosOnly));
                OnPropertyChanged(nameof(IsPhotosAndVideos));
            }
            OnPropertyChanged();
        }
    }

    private FileTypeProfile _selectedProfile = FileTypeProfile.PhotosOnly;
    public FileTypeProfile SelectedProfile
    {
        get => _selectedProfile;
        set { _selectedProfile = value; OnPropertyChanged(); }
    }

    // Режим работы
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

    // ========== Дубликаты ==========
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

    // ========== Очистка ==========
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

    // ========== Переименование ==========
    private string _renameFolder = "";
    public string RenameFolder
    {
        get => _renameFolder;
        set { _renameFolder = value; OnPropertyChanged(); OnPropertyChanged(nameof(RenamePreview)); }
    }

    private string _renamePattern = "Фото_{date}_{index}";
    public string RenamePattern
    {
        get => _renamePattern;
        set { _renamePattern = value; OnPropertyChanged(); OnPropertyChanged(nameof(RenamePreview)); }
    }

    private bool _isRenameRecursive = false;
    public bool IsRenameRecursive
    {
        get => _isRenameRecursive;
        set { _isRenameRecursive = value; OnPropertyChanged(); }
    }

    public ObservableCollection<string> RenameTemplates { get; } = new()
    {
        "Фото_{date}_{index}",
        "{year}/{month}/IMG_{index}",
        "Лето_{year}_{index}",
        "{date}_{name}",
        "{index}"
    };

    private string _selectedRenameTemplate = "Фото_{date}_{index}";
    public string SelectedRenameTemplate
    {
        get => _selectedRenameTemplate;
        set
        {
            _selectedRenameTemplate = value;
            RenamePattern = value;
            OnPropertyChanged();
        }
    }

    public string RenamePreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(RenameFolder) || string.IsNullOrWhiteSpace(RenamePattern))
                return "Пример: выберите папку и шаблон";

            try
            {
                var extensions = SupportedFormats.GetExtensionsByProfile(FileTypeProfile.AllSupported);
                var files = Directory.GetFiles(RenameFolder, "*.*",
                    IsRenameRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                    .Where(f => extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                    .ToArray();

                if (files.Length == 0)
                    return "Пример: подходящие файлы не найдены";

                var firstFile = files[0];
                var oldName = Path.GetFileNameWithoutExtension(firstFile);
                var ext = Path.GetExtension(firstFile);
                var dateTaken = MetadataService.GetPhotoDateTaken(firstFile) ?? File.GetCreationTime(firstFile);

                var preview = RenamePattern
                    .Replace("{date}", dateTaken.ToString("yyyyMMdd"))
                    .Replace("{year}", dateTaken.Year.ToString())
                    .Replace("{month}", dateTaken.ToString("MM"))
                    .Replace("{day}", dateTaken.ToString("dd"))
                    .Replace("{name}", oldName)
                    .Replace("{index}", "0001");

                foreach (var c in Path.GetInvalidFileNameChars())
                {
                    preview = preview.Replace(c.ToString(), "_");
                }

                return $"{preview}{ext}";
            }
            catch (Exception ex)
            {
                return $"Пример: ошибка ({ex.Message})";
            }
        }
    }

    // ========== Прогресс ==========
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

    // ========== Лог ==========
    public LogCollection Logger { get; } = new();
    public MainViewModel()
    {
        Logger.Log("🚀 Приложение запущено.", LogLevel.Info, "🚀");
    }
    // ========== INotifyPropertyChanged ==========
    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    
}