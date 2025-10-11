using Microsoft.Win32;
using PhotoSorterApp.Models;
using PhotoSorterApp.Services;
using PhotoSorterApp.ViewModels;
using PhotoSorterApp.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
#nullable disable

namespace PhotoSorterApp;

public partial class MainWindow : Window
{
    private CancellationTokenSource? _cts;

    public MainWindow()
    {
        InitializeComponent();
        if (DataContext is MainViewModel vm)
        {
            vm.Logger.CollectionChanged += (s, e) =>
            {
                LogScrollViewer?.ScrollToBottom();
            };
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
    }

    #region Вкладка: Сортировка

    private void SelectFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() == true)
        {
            if (DataContext is not MainViewModel vm) return;

            vm.SortingOptions = new SortingOptions
            {
                SourceFolder = dialog.FolderName,
                IsRecursive = vm.SortingOptions.IsRecursive,
                SplitByMonth = vm.SortingOptions.SplitByMonth,
                CreateBackup = vm.SortingOptions.CreateBackup
            };

            vm.Logger.Log($"Выбрана папка: {dialog.FolderName}");
        }
    }

    private async void StartProcess_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (string.IsNullOrWhiteSpace(vm.SortingOptions.SourceFolder))
        {
            MessageBox.Show("Выберите папку для сортировки.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (vm.IsSortOnly)
        {
            await StartSortingOnly(vm);
        }
        else if (vm.IsSortAndDuplicates)
        {
            await StartSortingAndDuplicates(vm);
        }
    }

    private async Task StartSortingOnly(MainViewModel vm)
    {
        var extensions = SupportedFormats.GetExtensionsByProfile(vm.SelectedProfile);
        var extSet = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase); // ← создаём HashSet

        var allFiles = Directory.GetFiles(vm.SortingOptions.SourceFolder, "*.*",
            vm.SortingOptions.IsRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Where(f => extSet.Contains(Path.GetExtension(f))) // ← теперь работает
            .ToList();
        int allFilesCount = allFiles.Count;

        vm.Logger.Log($"Начата сортировка по профилю '{vm.SelectedProfile}'...");
        vm.IsProgressVisible = true;
        vm.ProgressValue = 0;

        _cts = new CancellationTokenSource();
        var logProgress = new Progress<string>(msg => vm.Logger.Log(msg, LogLevel.Info));
        var progressPercent = new Progress<int>(value => vm.ProgressValue = value);

        int movedFiles = 0;
        try
        {
            await Task.Run(() =>
            {
                var sortingService = new PhotoSortingService(vm.Logger); // ← передаём LogCollection
                movedFiles = sortingService.SortPhotos(vm.SortingOptions, vm.SelectedProfile, logProgress, progressPercent, _cts.Token);
            });

            vm.Logger.Log($"✅ Сортировка завершена. Найдено файлов: {allFilesCount}, перемещено: {movedFiles}");
        }
        catch (Exception ex)
        {
            vm.Logger.Log($"❌ Критическая ошибка: {ex.Message}", LogLevel.Error);
            MessageBox.Show(
                $"Ошибка при обработке папки:\n{ex.Message}\n\n" +
                "Убедитесь, что выбрана обычная папка, а не корневой диск.",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            vm.IsProgressVisible = false;
            vm.ProgressValue = 0;
        }
    }

    private async Task StartSortingAndDuplicates(MainViewModel vm)
    {
        await StartSortingOnly(vm);

        vm.Logger.Log("Запуск поиска дубликатов...");
        var (groups, deleted, moved) = await ViewDuplicatesInternal(vm.SortingOptions.SourceFolder, vm.SortingOptions.IsRecursive, vm.SelectedProfile);
        if (groups > 0)
        {
            vm.Logger.Log($"✅ Дубликаты: найдено групп — {groups}, удалено файлов — {deleted}, перемещено — {moved}");
        }
    }

    #endregion

    #region Вкладка: Дубликаты

    private void SelectDuplicatesFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() == true && DataContext is MainViewModel vm)
        {
            vm.DuplicatesSearchFolder = dialog.FolderName;
        }
    }

    private async void FindDuplicates_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (string.IsNullOrWhiteSpace(vm.DuplicatesSearchFolder))
        {
            MessageBox.Show("Выберите папку для поиска дубликатов.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        vm.Logger.Log("Запуск поиска дубликатов...");
        var (groups, deleted, moved) = await ViewDuplicatesInternal(vm.DuplicatesSearchFolder, vm.IsDuplicatesRecursive, vm.SelectedProfile);
        if (groups > 0)
        {
            vm.Logger.Log($"✅ Дубликаты: найдено групп — {groups}, удалено файлов — {deleted}, перемещено — {moved}");
        }
    }

    #endregion

    #region Вкладка: Очистка

    private void SelectCleanupFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() == true && DataContext is MainViewModel vm)
        {
            vm.CleanupFolder = dialog.FolderName;
        }
    }

    private void StartCleanup_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (string.IsNullOrWhiteSpace(vm.CleanupFolder))
        {
            MessageBox.Show("Выберите папку для очистки.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var folder = vm.CleanupFolder;
        var quarantineDir = Path.Combine(folder, $"Карантин_{DateTime.Now:yyyyMMdd_HHmm}");
        Directory.CreateDirectory(quarantineDir);

        int movedCount = 0;

        try
        {
            var allFiles = Directory.GetFiles(folder, "*.*",
                vm.CleanupRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

            foreach (var file in allFiles)
            {
                var fileName = Path.GetFileName(file).ToLowerInvariant();
                var ext = Path.GetExtension(file).ToLowerInvariant();
                var fileSize = new FileInfo(file).Length;

                bool isScreenshot = vm.CleanupScreenshots &&
                    (fileName.Contains("screenshot") ||
                     fileName.Contains("скриншот") ||
                     fileName.Contains("capture") ||
                     fileName.Contains("screen"));

                bool isTempFile = vm.CleanupTempFiles &&
                    (fileName.StartsWith("~$") ||
                     ext == ".tmp" ||
                     ext == ".bak" ||
                     ext == ".lock");

                bool isEmptyFile = vm.CleanupEmptyFiles && (fileSize == 0);

                if (isScreenshot || isTempFile || isEmptyFile)
                {
                    var dest = Path.Combine(quarantineDir, Path.GetFileName(file));
                    File.Move(file, dest);
                    movedCount++;
                }
            }

            if (movedCount > 0)
            {
                vm.Logger.Log($"✅ Очистка завершена. Перемещено в Карантин: {movedCount} файлов");
                MessageBox.Show($"Перемещено файлов: {movedCount}\nКарантин: {quarantineDir}",
                              "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                vm.Logger.Log("🧹 Очистка: мусор не найден.");
                MessageBox.Show("Мусор не найден.", "Информация");
            }
        }
        catch (Exception ex)
        {
            vm.Logger.Log($"❌ Ошибка очистки: {ex.Message}", LogLevel.Error);
            MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region Вкладка: Переименование

    private void SelectRenameFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() == true && DataContext is MainViewModel vm)
        {
            vm.RenameFolder = dialog.FolderName;
        }
    }

    private void ApplyRename_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (string.IsNullOrWhiteSpace(vm.RenameFolder))
        {
            MessageBox.Show("Выберите папку для переименования.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(vm.RenamePattern))
        {
            MessageBox.Show("Введите шаблон переименования.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            "Внимание! Эта операция изменит имена файлов.\nРекомендуется создать резервную копию.\nПродолжить?",
            "Подтверждение",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            int renamedCount = 0;
            var allFiles = Directory.GetFiles(vm.RenameFolder, "*.*",
                vm.IsRenameRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

            var filesByDirectory = allFiles.GroupBy(f => Path.GetDirectoryName(f)!);

            foreach (var dirGroup in filesByDirectory)
            {
                var dir = dirGroup.Key;
                var filesInDir = dirGroup.ToList();

                for (int i = 0; i < filesInDir.Count; i++)
                {
                    var file = filesInDir[i];
                    var oldName = Path.GetFileNameWithoutExtension(file);
                    var ext = Path.GetExtension(file);

                    DateTime dateTaken = MetadataService.GetPhotoDateTaken(file) ?? File.GetCreationTime(file);

                    var newName = vm.RenamePattern
                        .Replace("{date}", dateTaken.ToString("yyyyMMdd"))
                        .Replace("{year}", dateTaken.Year.ToString())
                        .Replace("{month}", dateTaken.ToString("MM"))
                        .Replace("{day}", dateTaken.ToString("dd"))
                        .Replace("{name}", oldName)
                        .Replace("{index}", (i + 1).ToString("D4"));

                    var newFullPath = Path.Combine(dir, newName + ext);

                    if (File.Exists(newFullPath))
                    {
                        var counter = 1;
                        do
                        {
                            newFullPath = Path.Combine(dir, $"{newName}_{counter}{ext}");
                            counter++;
                        } while (File.Exists(newFullPath));
                    }

                    File.Move(file, newFullPath);
                    renamedCount++;
                }
            }

            vm.Logger.Log($"✅ Переименование завершено. Обработано файлов: {renamedCount}");
            MessageBox.Show($"Переименовано файлов: {renamedCount}", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            vm.Logger.Log($"❌ Ошибка переименования: {ex.Message}", LogLevel.Error);
            MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region Меню

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("PhotoSorter v1.0\nСортировка фото по дате и поиск дубликатов.", "О программе");
    }

    private void Formats_Click(object sender, RoutedEventArgs e)
    {
        var text = @"
Поддерживаемые форматы:

📸 Фото: JPG, JPEG, PNG, BMP, TIFF, CR2, CR3, NEF, ARW, DNG и др.
🎥 Видео: MP4, MOV, AVI, MKV, WMV, M4V
📄 Документы: PDF, DOCX, XLSX (для сортировки по дате)

Дубликаты ищутся по содержимому (хеш SHA256).
";
        MessageBox.Show(text, "Поддерживаемые форматы", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void FullHelp_Click(object sender, RoutedEventArgs e)
    {
        var helpPath = Path.Combine(Path.GetTempPath(), "PhotoSorter_Help.html");
        File.WriteAllText(helpPath, GenerateHelpHtml());
        Process.Start(new ProcessStartInfo
        {
            FileName = helpPath,
            UseShellExecute = true
        });
    }

    #endregion

    #region Вспомогательные методы

    private async Task<(int groups, int deleted, int moved)> ViewDuplicatesInternal(string folderPath, bool isRecursive, FileTypeProfile profile)
    {
        if (DataContext is not MainViewModel vm)
        {
            return (0, 0, 0);
        }

        var progressBar = new ProgressBar { Height = 12, IsIndeterminate = true, Margin = new Thickness(20, 10, 20, 0) };
        var statusText = new TextBlock
        {
            Text = "Поиск дубликатов...",
            Margin = new Thickness(20, 20, 20, 10),
            FontSize = 14
        };

        var layout = new StackPanel { Children = { statusText, progressBar } };

        var progressWindow = new Window
        {
            Title = "Поиск дубликатов",
            Content = layout,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = System.Windows.ResizeMode.NoResize,
            Width = 350
        };
        progressWindow.Show();

        try
        {
            var extensions = SupportedFormats.GetExtensionsByProfile(profile);
            var duplicates = await Task.Run(() =>
                new DuplicateDetectionService().FindDuplicatesWithExtensions(folderPath, isRecursive, extensions)
            );

            progressWindow.Close();

            if (duplicates.Count == 0)
            {
                MessageBox.Show("Дубликаты не найдены.", "Результат");
                return (0, 0, 0);
            }

            var duplicateWindow = new DuplicateWindow(duplicates);
            if (duplicateWindow.ShowDialog() == true)
            {
                return (duplicates.Count, duplicateWindow.DeletedCount, duplicateWindow.MovedCount);
            }
            return (duplicates.Count, 0, 0);
        }
        catch (Exception ex)
        {
            progressWindow.Close();
            MessageBox.Show($"Ошибка поиска дубликатов: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            return (0, 0, 0);
        }
    }

    private string GenerateHelpHtml()
    {
        return @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>PhotoSorter — Полная справка</title>
    <style>
        body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 40px; background: #fff; color: #333; line-height: 1.6; }
        h1 { color: #0078D7; border-bottom: 2px solid #eee; padding-bottom: 10px; }
        h2 { color: #333; margin-top: 30px; }
        h3 { color: #555; margin-top: 20px; }
        .section { margin-bottom: 30px; }
        code { background: #f5f5f5; padding: 2px 6px; border-radius: 4px; font-family: Consolas, monospace; }
        .tip { background: #e6f4ff; border-left: 4px solid #0078D7; padding: 15px; margin: 15px 0; }
        ul { padding-left: 20px; }
        li { margin-bottom: 8px; }
    </style>
</head>
<body>
    <h1>PhotoSorter — Полная справка</h1>
    
    <div class='section'>
        <h2>1. Общие принципы</h2>
        <p>PhotoSorter — это инструмент для безопасного управления цифровым архивом фотографий, видео и документов.</p>
        <div class='tip'>
            <strong>Важно:</strong> Программа <strong>никогда не удаляет файлы безвозвратно</strong>. Все операции (дубликаты, очистка) перемещают файлы в папку <code>Карантин_ГГГГММДД_ЧЧММ</code>.
        </div>
    </div>

    <div class='section'>
        <h2>2. Вкладка «Сортировка»</h2>
        <h3>Как это работает</h3>
        <ul>
            <li>Программа ищет файлы по выбранному профилю (Фото / Видео / Все)</li>
            <li>Определяет дату съёмки: сначала из EXIF, затем — дата создания файла</li>
            <li>Создаёт структуру: <code>Год/</code> или <code>Год/Месяц/</code></li>
        </ul>
        <h3>Настройки</h3>
        <ul>
            <li><strong>Рекурсивный поиск</strong> — обрабатывать подпапки</li>
            <li><strong>Создать бэкап</strong> — копия исходной папки перед сортировкой</li>
            <li><strong>Сортировка + дубликаты</strong> — автоматически запустить поиск дубликатов после сортировки</li>
        </ul>
    </div>

    <div class='section'>
        <h2>3. Вкладка «Дубликаты»</h2>
        <h3>Как это работает</h3>
        <ul>
            <li>Поиск по <strong>содержимому файла</strong> (хеш SHA256)</li>
            <li>В каждой группе файлы <strong>отсортированы по размеру</strong> (самый большой — первый)</li>
            <li>По умолчанию <strong>выбраны все, кроме первого</strong> (самого большого)</li>
        </ul>
        <h3>Удаление</h3>
        <p>При удалении вы можете выбрать:</p>
        <ul>
            <li><strong>Карантин</strong> — файлы перемещаются в папку <code>Карантин_...</code> в той же директории</li>
            <li><strong>Корзина</strong> — файлы удаляются в системную корзину</li>
        </ul>
    </div>

    <div class='section'>
        <h2>4. Вкладка «Переименование»</h2>
        <h3>Конструктор шаблонов</h3>
        <p>Собирайте имя файла из блоков:</p>
        <ul>
            <li><code>Текст</code> — произвольный текст («Фото_», «Лето_»)</li>
            <li><code>Дата</code> → <code>{date}</code> → 20240521</li>
            <li><code>Индекс</code> → <code>{index}</code> → 0001, 0002, ...</li>
            <li><code>Имя</code> → <code>{name}</code> → оригинальное имя файла</li>
            <li><code>Год</code>, <code>Месяц</code>, <code>День</code> — отдельные части даты</li>
        </ul>
        <h3>Примеры</h3>
        <ul>
            <li><code>Фото_{date}_{index}</code> → <code>Фото_20240521_0001.jpg</code></li>
            <li><code>{year}/{month}/IMG_{index}</code> → <code>2024/05/IMG_0001.jpg</code></li>
        </ul>
    </div>

    <div class='section'>
        <h2>5. Вкладка «Очистка»</h2>
        <p>Перемещает в Карантин:</p>
        <ul>
            <li><strong>Скриншоты</strong> — файлы с «screenshot», «скриншот», «capture» в имени</li>
            <li><strong>Временные файлы</strong> — <code>~$</code>, <code>.tmp</code>, <code>.bak</code></li>
            <li><strong>Пустые файлы</strong> — размер 0 байт</li>
        </ul>
    </div>

    <div class='section'>
        <h2>6. Поддерживаемые форматы</h2>
        <h3>📸 Фото</h3>
        <p>JPG, JPEG, PNG, BMP, TIFF, CR2, CR3, NEF, ARW, DNG и др.</p>
        <h3>🎥 Видео</h3>
        <p>MP4, MOV, AVI, MKV, WMV, M4V</p>
        <h3>📄 Документы</h3>
        <p>PDF, DOCX, XLSX, TXT</p>
    </div>

    <div class='section'>
        <h2>7. Безопасность и восстановление</h2>
        <ul>
            <li>Все удалённые файлы — в папке <code>Карантин_...</code></li>
            <li>Бэкап создаётся как папка <code>Backup_...</code> рядом с исходной</li>
            <li>Никаких безвозвратных операций без вашего подтверждения</li>
        </ul>
    </div>

    <hr>
    <p><em>PhotoSorter v1.0 — ваш надёжный архивариус</em></p>
</body>
</html>";
    }

    #endregion
}

public class OpenFolderDialog
{
    public string? FolderName { get; private set; }

    public bool? ShowDialog()
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog();
        dialog.Description = "Выберите папку (не корневой диск!)";

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var selected = dialog.SelectedPath;

            if (selected.Length == 3 && selected.EndsWith(":\\"))
            {
                MessageBox.Show(
                    "Нельзя выбирать корневой диск (C:\\, D:\\ и т.д.)!\n" +
                    "Выберите конкретную папку.",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return null;
            }

            FolderName = selected;
            return true;
        }
        return false;
    }
}