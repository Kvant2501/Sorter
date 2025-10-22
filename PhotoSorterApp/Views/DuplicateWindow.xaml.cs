using PhotoSorterApp.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace PhotoSorterApp.Views;

// Вспомогательные классы
public class GroupWrapper
{
    public List<FileItem> Items { get; set; } = new();
}

public class FileItem : INotifyPropertyChanged
{
    public string FilePath { get; set; } = "";

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

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// Основной класс — ДОЛЖЕН быть partial!
public partial class DuplicateWindow : Window
{
    private List<DuplicateGroup> _groups;
    private Window? _owner;

    public int DeletedCount { get; private set; }
    public int MovedCount { get; private set; }

    public DuplicateWindow(List<DuplicateGroup> groups, Window owner = null)
    {
        _groups = groups;
        _owner = owner;
        InitializeComponent(); // ← Обязательно!
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        var progressDialog = new ProgressDialog("Загрузка дубликатов", "Формирование списка...", () =>
        {
            Close();
        });

        if (_owner != null)
        {
            progressDialog.Owner = _owner;
        }

        progressDialog.Show();

        // ДАЁМ WPF ВРЕМЯ НА ОТРИСОВКУ ПРОГРЕСС-ОКНА
        await Task.Yield(); // ← Передаём управление обратно в UI-поток

        try
        {
            var data = await Task.Run(() =>
            {
                return CreateBindings(_groups);
            });

            DataContext = data;
        }
        finally
        {
            progressDialog.Close();
        }
    }

    private object CreateBindings(List<DuplicateGroup> groups)
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
                items.Add(new FileItem
                {
                    FilePath = sortedFiles[i].Path,
                    IsSelected = i > 0
                });
            }
            return new GroupWrapper { Items = items };
        }).ToList();

        return new { DuplicateGroups = wrappedGroups };
    }

    // === ОБЯЗАТЕЛЬНО: методы из XAML ===
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
        if (DataContext == null) return;

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