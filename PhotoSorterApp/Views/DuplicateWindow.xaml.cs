#nullable enable

using PhotoSorterApp.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;


namespace PhotoSorterApp.Views;

public class GroupWrapper
{
    public List<FileItem> Items { get; set; } = new();
}

public class FileItem : INotifyPropertyChanged
{
    private string _filePath = string.Empty; // ← Инициализация
    public string FilePath
    {
        get => _filePath;
        set
        {
            _filePath = value;
            if (!string.IsNullOrEmpty(value))
            {
                Debug.WriteLine($"Файл: {value}, Расширение: {Path.GetExtension(value)}");
            }
        }
    }

    public string FileName
    {
        get
        {
            var name = Path.GetFileName(FilePath);
            return name.Length > 30 ? name.Substring(0, 27) + "..." : name;
        }
    }

    public string DisplaySize
    {
        get
        {
            try
            {
                var size = new FileInfo(FilePath).Length;
                return size switch
                {
                    < 1024 => $"{size} Б",
                    < 1024 * 1024 => $"{size / 1024} КБ",
                    _ => $"{size / (1024 * 1024)} МБ"
                };
            }
            catch
            {
                return "—";
            }
        }
    }

    private BitmapImage? _preview;
    private bool _previewLoaded;

    /// <summary>
    /// Превью изображения. Возвращает null, пока не загрузится.
    /// </summary>
    public BitmapImage? Preview
    {
        get
        {
            if (!_previewLoaded)
            {
                _previewLoaded = true;
                _ = Task.Run(LoadPreviewAsync);
                return null;
            }
            return _preview;
        }
        private set { _preview = value; OnPropertyChanged(); }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    private async Task LoadPreviewAsync()
    {
        try
        {
            Debug.WriteLine($"Загрузка превью: {FilePath}");

            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            var bitmap = new WriteableBitmap(frame);

            // Масштабируем до 80x60
            var scale = Math.Min(80.0 / bitmap.PixelWidth, 60.0 / bitmap.PixelHeight);
            var resized = new TransformedBitmap(bitmap, new ScaleTransform(scale, scale));

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _preview = resized;
                OnPropertyChanged(nameof(Preview));
                Debug.WriteLine($"✅ Превью загружено: {FilePath}");
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка загрузки превью {FilePath}: {ex.Message}");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class DuplicateWindow : Window
{
    public int DeletedCount { get; private set; }
    public int MovedCount { get; private set; }

    public DuplicateWindow(List<DuplicateGroup> groups, Window owner = null)
    {
        Owner = owner;
        InitializeComponent();
        SetupBindings(groups);
    }

    private void SetupBindings(List<DuplicateGroup> groups)
    {
        var wrappedGroups = groups.Select(g =>
        {
            var sortedFiles = g.Files
                .Select(f => new { Path = f, Size = new FileInfo(f).Length })
                .OrderByDescending(x => x.Size)
                .ToList();

            var items = new List<FileItem>();
            for (int i = 0; i < sortedFiles.Count; i++)
            {
                var filePath = sortedFiles[i].Path;
                Debug.WriteLine($"Добавлен файл: {filePath}"); // ← Логируем путь
                items.Add(new FileItem
                {
                    FilePath = sortedFiles[i].Path,
                    IsSelected = i > 0
                });
            }
            return new GroupWrapper { Items = items };
        }).ToList();

        DataContext = new { DuplicateGroups = wrappedGroups };
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        await ProcessFilesAsync();
        DialogResult = true;
        Close();
    }

    private async void MoveSelected_Click(object sender, RoutedEventArgs e)
    {
        await ProcessFilesAsync();
        DialogResult = true;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async Task<bool> TryMoveFileAsync(string source, string dest, int maxRetries = 3, int delayMs = 500)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                if (File.Exists(dest))
                {
                    File.Delete(dest);
                }
                File.Move(source, dest);
                return true;
            }
            catch (IOException)
            {
                if (i == maxRetries - 1)
                    throw;
                await Task.Delay(delayMs);
            }
        }
        return false;
    }

    private async Task ProcessFilesAsync()
    {
        var context = (dynamic)DataContext;
        var groups = (IEnumerable<GroupWrapper>)context.DuplicateGroups;

        foreach (var group in groups)
        {
            foreach (var file in group.Items)
            {
                if (file.IsSelected && File.Exists(file.FilePath))
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(file.FilePath);
                        if (string.IsNullOrEmpty(dir)) continue;

                        var quarantine = Path.Combine(dir, $"Карантин_{DateTime.Now:yyyyMMdd_HHmm}");
                        Directory.CreateDirectory(quarantine);
                        var dest = Path.Combine(quarantine, Path.GetFileName(file.FilePath));

                        bool success = await TryMoveFileAsync(file.FilePath, dest);
                        if (success)
                        {
                            MovedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка перемещения {file.FileName}: {ex.Message}");
                    }
                }
            }
        }
    }
}