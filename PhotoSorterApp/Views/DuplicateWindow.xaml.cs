#nullable enable

using PhotoSorterApp.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private string _filePath = string.Empty;
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

    // Маленькое превью (80x60) — загружается лениво
    private BitmapSource? _preview;
    public BitmapSource? Preview
    {
        get
        {
            if (_preview == null && !_previewLoaded)
            {
                _previewLoaded = true;
                _ = LoadPreviewAsync();
            }
            return _preview;
        }
        private set { _preview = value; OnPropertyChanged(); }
    }

    private bool _previewLoaded;

    // Большое превью (300x200) — загружается при наведении
    private BitmapSource? _largePreview;
    public BitmapSource? LargePreview
    {
        get
        {
            if (_largePreview == null && !_largePreviewLoaded)
            {
                _largePreviewLoaded = true;
                _ = LoadLargePreviewAsync();
            }
            return _largePreview;
        }
        private set { _largePreview = value; OnPropertyChanged(); }
    }

    private bool _largePreviewLoaded;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    // Ограничение: максимум 5 одновременных загрузок
    private static readonly SemaphoreSlim _semaphore = new(5, 5);

    // Загрузка маленького превью
    internal async Task LoadPreviewAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            Debug.WriteLine($"Загрузка маленького превью: {FilePath}");

            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = stream;
            bitmap.DecodePixelWidth = 80;
            bitmap.DecodePixelHeight = 60;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Preview = bitmap;
                Debug.WriteLine($"✅ Маленькое превью загружено: {FilePath}");
            }, ct);
        }
        catch (OperationCanceledException)
        {
            // Игнорируем отмену
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка маленького превью {FilePath}: {ex.Message}");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    // Загрузка большого превью
    internal async Task LoadLargePreviewAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            Debug.WriteLine($"Загрузка большого превью: {FilePath}");

            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = stream;
            bitmap.DecodePixelWidth = 300;
            bitmap.DecodePixelHeight = 200;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                LargePreview = bitmap;
                Debug.WriteLine($"✅ Большое превью загружено: {FilePath}");
            }, ct);
        }
        catch (OperationCanceledException)
        {
            // Игнорируем отмену
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка большого превью {FilePath}: {ex.Message}");
        }
        finally
        {
            _semaphore.Release();
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

    // Источник токена для отмены
    private readonly CancellationTokenSource _cts = new();

    public DuplicateWindow(List<DuplicateGroup> groups, Window? owner = null)
    {
        Owner = owner;
        InitializeComponent();
        SetupBindings(groups);
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts.Cancel();
        _cts.Dispose();
        base.OnClosed(e);
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
                Debug.WriteLine($"Добавлен файл: {filePath}");
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

    private void OnImageMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Image image && image.DataContext is FileItem item)
        {
            _ = item.LoadLargePreviewAsync(_cts.Token);
        }
    }

    private void OnImageMouseLeave(object sender, MouseEventArgs e)
    {
        // Popup закроется автоматически
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