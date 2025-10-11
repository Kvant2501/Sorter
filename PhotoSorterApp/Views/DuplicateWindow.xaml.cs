using PhotoSorterApp.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace PhotoSorterApp.Views;

public partial class DuplicateWindow : Window
{
    public int DeletedCount { get; private set; }
    public int MovedCount { get; private set; }

    public DuplicateWindow(List<DuplicateGroup> groups)
    {
        InitializeComponent();
        Loaded += (s, e) => SetupBindings(groups);
    }

    private void SetupBindings(List<DuplicateGroup> groups)
    {
        var wrappedGroups = groups.Select(g =>
        {
            // Сортируем по размеру: самый большой — первый
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
                    IsSelected = i > 0 // первый — не выбран
                });
            }
            return new GroupWrapper { Items = items }; // ← Items есть у GroupWrapper, не у DuplicateGroup!
        }).ToList();

        DataContext = new { DuplicateGroups = wrappedGroups };
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        ProcessFiles();
        DialogResult = true;
        Close();
    }

    private void MoveSelected_Click(object sender, RoutedEventArgs e)
    {
        ProcessFiles();
        DialogResult = true;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }


    private void ProcessFiles()
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
                        File.Move(file.FilePath, dest);
                        MovedCount++;
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

    private void ShowInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is FileItem file)
        {
            if (File.Exists(file.FilePath))
            {
                var arg = $"/select,\"{file.FilePath}\"";
                Process.Start("explorer.exe", arg);
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