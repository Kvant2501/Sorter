#nullable enable

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
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace PhotoSorterApp;

public partial class MainWindow : Window
{
    private CancellationTokenSource? _cts;

    public MainWindow()
    {
        InitializeComponent();

        // Initialize theme menu state based on current application theme
        if (Application.Current is App app)
        {
            var theme = app.CurrentTheme?.ToLowerInvariant();
            if (theme == "dark")
            {
                ThemeDarkMenu.IsChecked = true;
                ThemeLightMenu.IsChecked = false;
            }
            else
            {
                ThemeLightMenu.IsChecked = true;
                ThemeDarkMenu.IsChecked = false;
            }
        }
    }

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi)
        {
            // Uncheck the other theme menu item
            if (mi == ThemeLightMenu)
                ThemeDarkMenu.IsChecked = false;
            else if (mi == ThemeDarkMenu)
                ThemeLightMenu.IsChecked = false;

            if (Application.Current is App app)
            {
                if (mi == ThemeDarkMenu)
                    app.ApplyTheme("Dark");
                else
                    app.ApplyTheme("Light");
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _cts?.Cancel();
        _cts?.Dispose();
    }

    #region Tab: Sorting

    private void SelectFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog())
        {
            if (DataContext is not MainViewModel vm) return;

            vm.SortingOptions = new SortingOptions
            {
                SourceFolder = dialog.FolderName ?? string.Empty,
                IsRecursive = vm.SortingOptions.IsRecursive,
                SplitByMonth = vm.SortingOptions.SplitByMonth,
                CreateBackup = vm.SortingOptions.CreateBackup
            };

            vm.Logger.Log($"📁 Выбрана папка: {dialog.FolderName}", LogLevel.Info, "📁");
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
        int movedFiles = 0;
        var errors = new List<string>();

        // Cancel any previous operation and create a new CTS
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        var progressDialog = new ProgressDialog("Сортировка", "Начата сортировка...");
        progressDialog.Owner = this;

        void OnCancel(object? s, EventArgs args)
        {
            _cts?.Cancel();
            vm.Logger.Log("⚠️ Сортировка отменена пользователем.", LogLevel.Warning, "⚠️");
        }

        progressDialog.CancelRequested += OnCancel;

        var progress = new Progress<int>(percent =>
        {
            try { progressDialog.UpdateStatus($"Сортировка: {percent}%"); }
            catch { }
        });

        try
        {
            progressDialog.Show();

            await Task.Run(() =>
            {
                try
                {
                    var service = ServiceLocator.CreatePhotoSortingService();
                    var result = service.SortPhotos(
                        vm.SortingOptions,
                        vm.SelectedProfile,
                        progress,
                        _cts.Token);

                    movedFiles = result.MovedFiles;
                    errors = result.Errors;
                }
                catch (OperationCanceledException)
                {
                    // Cancellation is expected
                }
                catch (Exception ex)
                {
                    errors.Add($"Internal error: {ex.Message}");
                }
            }, _cts.Token);

            if (!_cts.IsCancellationRequested)
            {
                vm.Logger.Log($"✅ Сортировка завершена. Перемещено файлов: {movedFiles}", LogLevel.Info, "✅");
                foreach (var error in errors)
                    vm.Logger.Log(error, LogLevel.Error, "❌");
            }
        }
        catch (OperationCanceledException)
        {
            vm.Logger.Log("⚠️ Сортировка отменена пользователем.", LogLevel.Warning, "⚠️");
        }
        catch (Exception ex)
        {
            vm.Logger.Log($"❌ Критическая ошибка: {ex.Message}", LogLevel.Error, "❌");
        }
        finally
        {
            progressDialog.CancelRequested -= OnCancel;
            progressDialog.Close();
        }
    }

    private async Task StartSortingAndDuplicates(MainViewModel vm)
    {
        await StartSortingOnly(vm);

        vm.Logger.Log("Запуск поиска дубликатов...", LogLevel.Info);

        // Recreate CTS so user can cancel the whole pipeline
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        // Show progress dialog for duplicate search so user sees activity (same UX as manual FindDuplicates)
        var progressDialog = new ProgressDialog("Поиск дубликатов", "Сканирование папки...");
        progressDialog.Owner = this;

        bool isCancelled = false;

        void OnCancel(object? s, EventArgs args)
        {
            isCancelled = true;
            _cts?.Cancel();
            vm.Logger.Log("⚠️ Поиск дубликатов отменён пользователем.", LogLevel.Warning, "⚠️");
        }

        progressDialog.CancelRequested += OnCancel;

        var progress = new Progress<(int processed, int total, string? current)>(t =>
        {
            try
            {
                if (t.total > 0)
                    progressDialog.UpdateStatus($"Сканирование: {t.processed}/{t.total}");
                else
                    progressDialog.UpdateStatus($"Сканировано: {t.processed} файлов");

                if (!string.IsNullOrEmpty(t.current))
                    progressDialog.UpdateDetail(Path.GetFileName(t.current));
            }
            catch { }
        });

        try
        {
            progressDialog.Show();

            List<DuplicateGroup>? duplicates = null;
            try
            {
                duplicates = await Task.Run(() =>
                {
                    var service = ServiceLocator.CreateDuplicateDetectionService();
                    var extensionsArray = SupportedFormats.GetExtensionsByProfile(vm.SelectedProfile);
                    var extensions = new HashSet<string>(extensionsArray, StringComparer.OrdinalIgnoreCase);

                    return service.FindDuplicatesWithExtensions(
                        vm.SortingOptions.SourceFolder,
                        vm.SortingOptions.IsRecursive,
                        extensions,
                        _cts.Token,
                        progress);
                }, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Cancellation already logged
                return;
            }

            if (isCancelled)
                return;

            if (duplicates != null && duplicates.Count > 0)
            {
                progressDialog.Close();
                var duplicateWindow = new DuplicateWindow(duplicates, vm.SortingOptions.SourceFolder, this);
                if (duplicateWindow.ShowDialog() == true)
                {
                    vm.Logger.Log($"✅ Дубликаты: найдено групп — {duplicates.Count}, удалено файлов — {duplicateWindow.DeletedCount}, перемещено — {duplicateWindow.MovedCount}", LogLevel.Info, "✅");
                }
            }
            else
            {
                progressDialog.Close();
                MessageBox.Show("Дубликаты не найдены.", "Результат");
            }
        }
        catch (OperationCanceledException)
        {
            vm.Logger.Log("⚠️ Поиск дубликатов отменён пользователем.", LogLevel.Warning, "⚠️");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ КРИТИЧЕСКАЯ ОШИБКА при поиске дубликатов: {ex.Message}");
            vm.Logger.Log($"❌ Критическая ошибка поиска: {ex.Message}", LogLevel.Error, "❌");
            MessageBox.Show($"Критическая ошибка: {ex.Message}\n\nДетали: {ex.StackTrace}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            progressDialog.CancelRequested -= OnCancel;
            progressDialog.Close();
        }
    }

    #endregion

    #region Tab: Duplicates

    private void SelectDuplicatesFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() && DataContext is MainViewModel vm)
        {
            vm.DuplicatesSearchFolder = dialog.FolderName ?? string.Empty;
        }
    }

    private async void FindDuplicates_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            MessageBox.Show("Ошибка инициализации приложения. Перезапустите программу.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(vm.DuplicatesSearchFolder))
        {
            MessageBox.Show("Выберите папку для поиска дубликатов.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!Directory.Exists(vm.DuplicatesSearchFolder))
        {
            MessageBox.Show($"Папка не существует: {vm.DuplicatesSearchFolder}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        vm.Logger.Log($"🔍 Поиск дубликатов в: {vm.DuplicatesSearchFolder}", LogLevel.Info, "🔍");

        // Cancel previous operation and create new CTS
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        var progressDialog = new ProgressDialog("Поиск дубликатов", "Сканирование папки...");
        progressDialog.Owner = this;

        bool isCancelled = false;

        void OnCancel(object? s, EventArgs args)
        {
            isCancelled = true;
            _cts?.Cancel();
            vm.Logger.Log("⚠️ Поиск дубликатов отменён пользователем.", LogLevel.Warning, "⚠️");
        }

        progressDialog.CancelRequested += OnCancel;

        // Progress reporter
        var progress = new Progress<(int processed, int total, string? current)>(t =>
        {
            try
            {
                if (t.total > 0)
                    progressDialog.UpdateStatus($"Сканирование: {t.processed}/{t.total}");
                else
                    progressDialog.UpdateStatus($"Сканировано: {t.processed} файлов");

                if (!string.IsNullOrEmpty(t.current))
                    progressDialog.UpdateDetail(Path.GetFileName(t.current));
            }
            catch { }
        });

        try
        {
            progressDialog.Show();

            var duplicatesTask = Task.Run(() =>
            {
                var service = ServiceLocator.CreateDuplicateDetectionService();
                var extensionsArray = SupportedFormats.GetExtensionsByProfile(vm.SelectedProfile);
                var extensions = new HashSet<string>(extensionsArray, StringComparer.OrdinalIgnoreCase);

                return service.FindDuplicatesWithExtensions(
                    vm.DuplicatesSearchFolder,
                    vm.IsDuplicatesRecursive,
                    extensions,
                    _cts.Token,
                    progress);
            }, _cts.Token);

            List<DuplicateGroup>? duplicates = null;
            try
            {
                duplicates = await duplicatesTask;
            }
            catch (OperationCanceledException)
            {
                // Cancellation already logged
                return;
            }

            if (isCancelled)
                return;

            if (duplicates != null && duplicates.Count > 0)
            {
                progressDialog.Close();
                var duplicateWindow = new DuplicateWindow(duplicates, vm.DuplicatesSearchFolder, this);
                if (duplicateWindow.ShowDialog() == true)
                {
                    vm.Logger.Log($"✅ Дубликаты: найдено групп — {duplicates.Count}, удалено файлов — {duplicateWindow.DeletedCount}, перемещено — {duplicateWindow.MovedCount}", LogLevel.Info, "✅");
                }
            }
            else
            {
                progressDialog.Close();
                MessageBox.Show("Дубликаты не найдены.", "Результат");
            }
        }
        catch (OperationCanceledException)
        {
            vm.Logger.Log("⚠️ Поиск дубликатов отменён пользователем.", LogLevel.Warning, "⚠️");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ КРИТИЧЕСКАЯ ОШИБКА: {ex.Message}");
            vm.Logger.Log($"❌ Критическая ошибка поиска: {ex.Message}", LogLevel.Error, "❌");
            MessageBox.Show($"Критическая ошибка: {ex.Message}\n\nДетали: {ex.StackTrace}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            progressDialog.CancelRequested -= OnCancel;
            progressDialog.Close();
        }
    }

    // SIMPLIFIED METHOD (kept for compatibility)
    private async Task<List<DuplicateGroup>?> GetDuplicateGroupsAsync(
     string folderPath,
     bool isRecursive,
     FileTypeProfile profile,
     CancellationToken cancellationToken)
    {
        if (DataContext is not MainViewModel vm)
            return null;

        try
        {
            var extensionsArray = SupportedFormats.GetExtensionsByProfile(profile);
            var extensions = new HashSet<string>(extensionsArray, StringComparer.OrdinalIgnoreCase);

            var service = ServiceLocator.CreateDuplicateDetectionService();
            return await Task.Run(() => service.FindDuplicatesWithExtensions(
                folderPath,
                isRecursive,
                extensions,
                cancellationToken
            ), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (DataContext is MainViewModel vm2)
                vm2.Logger?.Log($"Error loading duplicates: {ex.Message}", LogLevel.Error, "❌");
            return null;
        }
    }

    private async Task<(int groups, int deleted, int moved)> ViewDuplicatesInternalAsync(
        string folderPath,
        bool isRecursive,
        FileTypeProfile profile,
        CancellationToken cancellationToken)
    {
        if (DataContext is not MainViewModel vm)
        {
            return (0, 0, 0);
        }

        List<DuplicateGroup>? duplicates = null;
        bool loadSuccess = false;

        try
        {
            var service = ServiceLocator.CreateDuplicateDetectionService();
            var extensionsArray = SupportedFormats.GetExtensionsByProfile(profile);
            var extensions = new HashSet<string>(extensionsArray, StringComparer.OrdinalIgnoreCase);

            duplicates = await Task.Run(() => service.FindDuplicatesWithExtensions(
                    folderPath,
                    isRecursive,
                    extensions,
                    cancellationToken
                ), cancellationToken);

            loadSuccess = true;
        }
        catch (OperationCanceledException)
        {
            loadSuccess = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка загрузки дубликатов: {ex.Message}");
            loadSuccess = false;
        }

        if (loadSuccess && duplicates != null && duplicates.Count > 0)
        {
            var duplicateWindow = new DuplicateWindow(duplicates, folderPath, this);
            if (duplicateWindow.ShowDialog() == true)
            {
                return (duplicates.Count, duplicateWindow.DeletedCount, duplicateWindow.MovedCount);
            }
            return (duplicates.Count, 0, 0);
        }
        else if (loadSuccess && duplicates?.Count == 0)
        {
            MessageBox.Show("Дубликаты не найдены.", "Результат");
            return (0, 0, 0);
        }

        return (0, 0, 0);
    }

    #endregion

    #region Tab: Cleanup

    private void SelectCleanupFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() && DataContext is MainViewModel vm)
        {
            vm.CleanupFolder = dialog.FolderName ?? string.Empty;
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
        vm.Logger.Log($"🧹 Очистка папки: {vm.CleanupFolder}", LogLevel.Info, "🧹");
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
                vm.Logger.Log($"✅ Очистка завершена. Перемещено в Карантин: {movedCount} файлов", LogLevel.Info, "✅");
                MessageBox.Show($"Перемещено файлов: {movedCount}\nКарантин: {quarantineDir}",
                              "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                vm.Logger.Log("🧹 Очистка: мусор не найден.", LogLevel.Info, "🧹");
                MessageBox.Show("Мусор не найден.", "Информация");
            }
        }
        catch (Exception ex)
        {
            vm.Logger.Log($"❌ Ошибка очистки: {ex.Message}", LogLevel.Error, "❌");
            MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region Tab: Rename

    private void SelectRenameFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() && this.DataContext is MainViewModel vm)
        {
            vm.RenameFolder = dialog.FolderName ?? string.Empty;
        }
    }

    private void ApplyRename_Click(object sender, RoutedEventArgs e)
    {
        if (this.DataContext is not MainViewModel vm) return;
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

        vm.Logger.Log($"📝 Применяю шаблон: {vm.RenamePattern} в папке: {vm.RenameFolder}", LogLevel.Info, "📝");
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

                    foreach (var c in Path.GetInvalidFileNameChars())
                    {
                        newName = newName.Replace(c.ToString(), "_");
                    }

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

            vm.Logger.Log($"✅ Переименование завершено. Обработано файлов: {renamedCount}", LogLevel.Info, "✅");
            MessageBox.Show($"Переименовано файлов: {renamedCount}", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            vm.Logger.Log($"❌ Ошибка переименования: {ex.Message}", LogLevel.Error, "❌");
            MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void InsertBlock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is string block)
        {
            if (this.DataContext is MainViewModel vm)
            {
                vm.RenamePattern += block;
            }
        }
    }

    private void InsertCustomText_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new InputDialog("Введите текст:", "");
        if (dialog.ShowDialog() == true)
        {
            if (this.DataContext is MainViewModel vm)
            {
                vm.RenamePattern += dialog.Input;
            }
        }
    }

    private void FileType_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string profileName)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.SelectedProfile = profileName switch
                {
                    "PhotosOnly" => FileTypeProfile.PhotosOnly,
                    "VideosOnly" => FileTypeProfile.VideosOnly,
                    "PhotosAndVideos" => FileTypeProfile.PhotosAndVideos,
                    "AllSupported" => FileTypeProfile.AllSupported,
                    _ => FileTypeProfile.PhotosOnly
                };
            }
        }
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb)
        {
            if (rb.Tag as string == "SortOnly")
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.IsSortOnly = true;
                    vm.IsSortAndDuplicates = false;
                }
            }
            else if (rb.Tag as string == "SortAndDuplicates")
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.IsSortOnly = false;
                    vm.IsSortAndDuplicates = true;
                }
            }
        }
    }

    private void SaveLog_Click(object sender, RoutedEventArgs e)
    {
        var vm = DataContext as MainViewModel;
        if (vm?.Logger?.Count > 0)
        {
            var dialog = new SaveFileDialog();
            dialog.Filter = "Текстовые файлы (*.txt)|*.txt|Все файлы (*.*)|*.*";
            dialog.FileName = $"PhotoSorter_Log_{DateTime.Now:yyyyMMdd_HHmm}.txt";

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var lines = vm.Logger.Select(entry => $"[{entry.Timestamp}] {entry.Icon} {entry.Message}").ToArray();
                    File.WriteAllLines(dialog.FileName, lines);
                    MessageBox.Show("Лог сохранён.", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private void ThemeLight_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.ApplyTheme("Light");
            ThemeLightMenu.IsChecked = true;
            ThemeDarkMenu.IsChecked = false;
        }
    }

    private void ThemeDark_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.ApplyTheme("Dark");
            ThemeDarkMenu.IsChecked = true;
            ThemeLightMenu.IsChecked = false;
        }
    }

    #endregion

    #region Menu

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

    #region Helper methods

    private string GenerateHelpHtml()
    {
        return @"<!DOCTYPE html>
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

    #region Folder selection dialog (helper class)

    /// <summary>
    /// Simple wrapper around FolderBrowserDialog for folder selection.
    /// Does not allow selecting a root drive (e.g. C:\).
    /// </summary>
    public class OpenFolderDialog
    {
        public string? FolderName { get; private set; }

        public bool ShowDialog()
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
                    return false;
                }

                FolderName = selected;
                return true;
            }
            return false;
        }
    }

    #endregion
}