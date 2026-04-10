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
    private Process? _webServerProcess;

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

        try
        {
            if (_webServerProcess is { HasExited: false })
            {
                _webServerProcess.Kill(true);
                _webServerProcess.Dispose();
            }
        }
        catch
        {
            // ignore process shutdown errors
        }
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

        if (vm.SelectedProfile == FileTypeProfile.DocumentsOnly)
        {
            await StartDocumentsSortingFromSortingTab(vm);
            return;
        }

        await StartSortingOnly(vm);
    }

    private async Task StartSortingOnly(MainViewModel vm)
    {
        int movedFiles = 0;
        var errors = new List<string>();

        // Cancel any previous operation and create a new CTS
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        string profileLabel = vm.SelectedProfileDisplayName;
        string profileIcon = vm.SelectedProfile switch
        {
            FileTypeProfile.PhotosOnly => "📸",
            FileTypeProfile.VideosOnly => "🎥",
            FileTypeProfile.PhotosAndVideos => "📁",
            _ => "📁"
        };

        var progressDialog = new ProgressDialog("Сортировка", $"Начата сортировка: {profileLabel}...");
        progressDialog.Owner = this;

        void OnCancel(object? s, EventArgs args)
        {
            _cts?.Cancel();
            vm.Logger.Log($"⚠️ [{profileLabel}] Сортировка.cancelled пользователем.", LogLevel.Warning, "⚠️");
        }

        progressDialog.CancelRequested += OnCancel;

        var progress = new Progress<int>(percent =>
        {
            try { progressDialog.UpdateStatus($"{profileLabel}: {percent}%"); }
            catch { }
        });

        try
        {
            progressDialog.Show();

            await Task.Run(() =>
            {
                try
                {
                    var service = ServiceLocator.CreatePhotoSortingService(msg =>
                    {
                        vm.Logger.Log($"{profileIcon} [{profileLabel}] {msg}", LogLevel.Info, profileIcon);
                    });

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
                    // expected
                }
                catch (Exception ex)
                {
                    errors.Add($"Internal error: {ex.Message}");
                }
            }, _cts.Token);

            if (!_cts.IsCancellationRequested)
            {
                vm.Logger.Log($"✅ [{profileLabel}] Сортировка завершена. Перемещено файлов: {movedFiles}", LogLevel.Info, "✅");
                foreach (var error in errors)
                    vm.Logger.Log($"❌ [{profileLabel}] {error}", LogLevel.Error, "❌");
            }
        }
        catch (OperationCanceledException)
        {
            vm.Logger.Log($"⚠️ [{profileLabel}] Сортировка.cancelled пользователем.", LogLevel.Warning, "⚠️");
        }
        catch (Exception ex)
        {
            vm.Logger.Log($"❌ [{profileLabel}] Критическая ошибка: {ex.Message}", LogLevel.Error, "❌");
        }
        finally
        {
            progressDialog.CancelRequested -= OnCancel;
            progressDialog.Close();
        }
    }

    private async Task StartDocumentsSortingFromSortingTab(MainViewModel vm)
    {
        int movedFiles = 0;
        var errors = new List<string>();

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        var progressDialog = new ProgressDialog("Документы", "Начата сортировка документов...");
        progressDialog.Owner = this;

        void OnCancel(object? s, EventArgs args)
        {
            _cts?.Cancel();
            vm.Logger.Log("⚠️ [Документы] Сортировка.cancelled пользователем.", LogLevel.Warning, "⚠️");
        }

        progressDialog.CancelRequested += OnCancel;

        var progress = new Progress<int>(percent =>
        {
            try { progressDialog.UpdateStatus($"Документы: {percent}%"); }
            catch { }
        });

        try
        {
            progressDialog.Show();

            await Task.Run(() =>
            {
                try
                {
                    var service = ServiceLocator.CreateDocumentSortingService(msg =>
                    {
                        vm.Logger.Log($"📄 [Документы] {msg}", LogLevel.Info, "📄");
                    });

                    var result = service.SortDocuments(vm.SortingOptions, progress, _cts.Token);
                    movedFiles = result.MovedFiles;
                    errors = result.Errors;
                }
                catch (OperationCanceledException)
                {
                    // expected
                }
                catch (Exception ex)
                {
                    errors.Add($"Internal error: {ex.Message}");
                }
            }, _cts.Token);

            if (!_cts.IsCancellationRequested)
            {
                vm.Logger.Log($"✅ [Документы] Сортировка завершена. Перемещено файлов: {movedFiles}", LogLevel.Info, "✅");
                foreach (var error in errors)
                    vm.Logger.Log($"❌ [Документы] {error}", LogLevel.Error, "❌");
            }
        }
        catch (OperationCanceledException)
        {
            vm.Logger.Log("⚠️ [Документы] Сортировка.cancelled пользователем.", LogLevel.Warning, "⚠️");
        }
        catch (Exception ex)
        {
            vm.Logger.Log($"❌ [Документы] Критическая ошибка: {ex.Message}", LogLevel.Error, "❌");
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
                var extensionsArray = SupportedFormats.GetExtensionsByProfile(vm.DuplicatesProfile);
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

        var folder = vm.CleanupFolder;

        if (!Directory.Exists(folder))
        {
            MessageBox.Show($"Папка не существует: {folder}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Запрещаем выбор корня диска
        var root = Path.GetPathRoot(folder);
        if (!string.IsNullOrEmpty(root) && string.Equals(root.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "Нельзя выбирать корневой диск (C:\\, D:\\ и т.д.).\nВыберите конкретную папку.",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        vm.Logger.Log($"🧹 Начало очистки папки: {folder}", LogLevel.Info, "🧹");

        // 1) Собираем кандидатов
        var candidates = new List<(string path, string reason)>();

        string[] allFiles;
        try
        {
            allFiles = Directory.GetFiles(folder, "*.*",
                vm.CleanupRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show(
                "Нет прав на чтение одной или нескольких подпапок.\n" +
                "Попробуйте отключить рекурсивный поиск или выберите другую папку.",
                "Доступ запрещён",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        vm.Logger.Log($"📊 Найдено файлов: {allFiles.Length}", LogLevel.Info, "📊");

        foreach (var file in allFiles)
        {
            try
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
                     ext == ".lock" ||
                     ext == ".dwl" ||
                     ext == ".dwl2" ||
                     ext == ".asd" ||
                     ext == ".wbk" ||
                     ext == ".swp" ||
                     ext == ".crdownload" ||
                     ext == ".download" ||
                     ext == ".part");

                bool isEmptyFile = vm.CleanupEmptyFiles && (fileSize == 0);

                if (isScreenshot || isTempFile || isEmptyFile)
                {
                    var reason = isScreenshot ? "скриншот" : isTempFile ? "временный" : "пустой";
                    candidates.Add((file, reason));
                }
            }
            catch (UnauthorizedAccessException)
            {
                vm.Logger.Log($"⛔ Доступ запрещён: {file}", LogLevel.Warning, "⛔");
            }
            catch (IOException)
            {
                vm.Logger.Log($"🔒 Файл занят/недоступен: {file}", LogLevel.Warning, "🔒");
            }
            catch (Exception ex)
            {
                vm.Logger.Log($"❌ Ошибка проверки {file}: {ex.Message}", LogLevel.Error, "❌");
            }
        }

        if (candidates.Count == 0)
        {
            vm.Logger.Log("🧹 Очистка: мусор не найден.", LogLevel.Info, "🧹");
            MessageBox.Show("Мусор не найден.", "Информация");
            return;
        }

        // Логируем найденное (для прозрачности)
        vm.Logger.Log($"🧹 Очистка: найдено кандидатов — {candidates.Count}.", LogLevel.Info, "🧹");
        foreach (var (path, reason) in candidates)
        {
            try
            {
                vm.Logger.Log($"   • {reason}: {path}", LogLevel.Info, "🧹");
            }
            catch { }
        }

        // Подтверждение перед переносом
        var confirm = MessageBox.Show(
            $"Найдено файлов: {candidates.Count}\n\nПереместить в карантин?",
            "Очистка — подтверждение",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            vm.Logger.Log("🧹 Очистка отменена пользователем.", LogLevel.Info, "🧹");
            return;
        }

        // 2) Только теперь создаём карантин
        string quarantineDir;
        try
        {
            quarantineDir = Path.Combine(folder, $"Карантин_{DateTime.Now:yyyyMMdd_HHmm}");
            Directory.CreateDirectory(quarantineDir);
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show(
                "Нет прав на создание папки карантина в выбранной директории.\n" +
                "Выберите другую папку или запустите приложение от имени администратора.",
                "Доступ запрещён",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось создать папку карантина: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // 3) Перемещаем
        int movedCount = 0;
        int failedCount = 0;

        foreach (var (path, reason) in candidates)
        {
            try
            {
                var dest = Path.Combine(quarantineDir, Path.GetFileName(path));

                if (File.Exists(dest))
                {
                    var baseName = Path.GetFileNameWithoutExtension(dest);
                    var destExt = Path.GetExtension(dest);
                    var i = 1;
                    string candidate;
                    do
                    {
                        candidate = Path.Combine(quarantineDir, $"{baseName}_{i}{destExt}");
                        i++;
                    } while (File.Exists(candidate));
                    dest = candidate;
                }

                File.Move(path, dest);
                movedCount++;
                vm.Logger.Log($"🗑️ В карантин ({reason}): {Path.GetFileName(path)}", LogLevel.Info, "🗑️");
            }
            catch (UnauthorizedAccessException)
            {
                failedCount++;
                vm.Logger.Log($"⛔ Доступ запрещён (пропуск): {path}", LogLevel.Warning, "⛔");
            }
            catch (IOException)
            {
                failedCount++;
                vm.Logger.Log($"🔒 Файл занят/недоступен (пропуск): {path}", LogLevel.Warning, "🔒");
            }
            catch (Exception ex)
            {
                failedCount++;
                vm.Logger.Log($"❌ Ошибка перемещения {path}: {ex.Message}", LogLevel.Error, "❌");
            }
        }

        if (movedCount == 0)
        {
            // Ничего не перемещено — чтобы не плодить пустые папки, удаляем карантин
            try
            {
                if (Directory.Exists(quarantineDir) && !Directory.EnumerateFileSystemEntries(quarantineDir).Any())
                    Directory.Delete(quarantineDir);
            }
            catch { }

            vm.Logger.Log($"🧹 Очистка: найдено файлов — {candidates.Count}, но переместить не удалось (ошибок: {failedCount}).", LogLevel.Warning, "🧹");
            MessageBox.Show(
                $"Найдено файлов: {candidates.Count}\nПеремечено: 0\nОшибок: {failedCount}\n\n" +
                "Файлы могли быть заняты другими программами или недоступны по правам.",
                "Результат очистки",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        vm.Logger.Log($"✅ Очистка завершена. Найдено: {candidates.Count}. Перемещено в Карантин: {movedCount}. Ошибок: {failedCount}.", LogLevel.Info, "✅");
        MessageBox.Show(
            $"Найдено файлов: {candidates.Count}\nПеремещено: {movedCount}\nОшибок: {failedCount}\n\nКарантин: {quarantineDir}",
            "Готово",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
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
                    "DocumentsOnly" => FileTypeProfile.DocumentsOnly,
                    _ => FileTypeProfile.PhotosOnly
                };
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

    #region Tab: Catalog

    private void SelectCatalogFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() == true)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.CatalogFolder = dialog.FolderName ?? string.Empty;
            }
        }
    }

    private void GenerateCatalog_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var catalogFolder = vm.CatalogFolder;
        if (string.IsNullOrWhiteSpace(catalogFolder))
        {
            MessageBox.Show("Выберите папку для создания каталога.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!Directory.Exists(catalogFolder))
        {
            MessageBox.Show($"Папка не существует: {catalogFolder}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        vm.Logger.Log($"📂 Создание HTML-каталога для: {catalogFolder}", LogLevel.Info, "📂");

        try
        {
            var includeFiles = CatalogIncludeFilesCheckBox.IsChecked == true;
            var includeSize = CatalogIncludeSizeCheckBox.IsChecked == true;

            vm.Logger.Log($"📊 Параметры: файлы={includeFiles}, размеры={includeSize}", LogLevel.Info, "📊");

            var htmlCatalog = DirectoryCatalogGenerator.GenerateCatalog(catalogFolder, includeFiles, includeSize);

            var catalogPath = Path.Combine(catalogFolder, $"catalog_{DateTime.Now:yyyyMMdd_HHmmss}.html");
            File.WriteAllText(catalogPath, htmlCatalog, System.Text.Encoding.UTF8);

            vm.Logger.Log($"✅ Каталог создан: {catalogPath}", LogLevel.Info, "✅");

            MessageBox.Show(
                $"HTML-каталог успешно создан:\n\n{catalogPath}\n\nДля просмотра используйте кнопку \"Открыть веб-галерею\".",
                "Готово",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            vm.Logger.Log($"❌ Ошибка создания каталога: {ex.Message}", LogLevel.Error, "❌");
            MessageBox.Show($"Ошибка создания каталога:\n\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region Tab: Faces

    private void SelectFaceIndexFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() == true && DataContext is MainViewModel vm)
        {
            vm.FaceIndexFolder = dialog.FolderName ?? string.Empty;
            vm.Logger.Log($"📸 Папка для распознавания: {vm.FaceIndexFolder}", LogLevel.Info, "📸");
        }
    }

    private async void StartFaceIndexing_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (string.IsNullOrWhiteSpace(vm.FaceIndexFolder) || !Directory.Exists(vm.FaceIndexFolder))
        {
            MessageBox.Show("Выберите существующую папку с фото.", "Лица", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var healthUrl = ResolveFaceApiHealthUrl();
        if (!await IsFaceApiAvailableAsync(healthUrl))
        {
            vm.Logger.Log($"❌ Face API недоступен: {healthUrl}", LogLevel.Error, "❌");
            MessageBox.Show($"Face API недоступен ({healthUrl}).\nЗапустите контейнер Docker и повторите.", "Лица", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var progressDialog = new ProgressDialog("Лица", "Запущена индексация лиц...");
        progressDialog.Owner = this;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        void OnCancel(object? _, EventArgs __) => _cts?.Cancel();
        progressDialog.CancelRequested += OnCancel;

        var progress = new Progress<int>(percent =>
        {
            try { progressDialog.UpdateStatus($"Лица: {percent}%"); } catch { }
        });

        try
        {
            progressDialog.Show();
            var pipeline = ServiceLocator.CreateFaceIndexingPipelineService(msg =>
            {
                if (!vm.FaceDetailedLogging && IsFaceDiagnosticLogMessage(msg))
                    return;

                vm.Logger.Log($"🤖 {msg}", LogLevel.Info, "🤖");
            });
            var thresholdSlider = this.FindName("ThresholdSlider") as Slider;
            var threshold = thresholdSlider?.Value ?? 0.55;
            
            var result = await pipeline.RunAsync(new FaceIndexingOptions
            {
                SourceFolder = vm.FaceIndexFolder,
                IsRecursive = vm.FaceIndexRecursive,
                MinConfidence = vm.FaceMinConfidence,
                BatchSize = vm.FaceBatchSize,
                AutoAssignKnownPersons = true,
                AutoAssignThreshold = threshold,
                SkipAlreadyIndexedPhotos = !vm.FaceIndexRescan // ← Инвертируем: если Rescan=true, то Skip=false
            }, progress, _cts.Token);

            vm.Logger.Log($"✅ Индексация лиц завершена. Файлов: {result.ProcessedFiles}, лиц: {result.SavedFaces}", LogLevel.Info, "✅");
            vm.Logger.Log($"📊 Сводка: с лицами={result.FilesWithAcceptedFaces}, без лиц={result.FilesWithoutAcceptedFaces}, автоназначено={result.AutoAssignedFaces}, unknown={result.UnknownFaces}", LogLevel.Info, "📊");
            vm.Logger.Log($"📊 Служебно: пропущено без изменений={result.SkippedAlreadyIndexedPhotos}, переиндексировано из-за отсутствия лиц={result.ReindexedBecauseMissingFaces}", LogLevel.Info, "📊");

            vm.FaceLastRunSummary =
                $"Последний запуск: файлов {result.ProcessedFiles}, лиц {result.SavedFaces}, автоназначено {result.AutoAssignedFaces}, unknown {result.UnknownFaces}, фото с лицами {result.FilesWithAcceptedFaces}, без лиц {result.FilesWithoutAcceptedFaces}.";

            foreach (var error in result.Errors.Take(20))
                vm.Logger.Log($"❌ {error}", LogLevel.Error, "❌");

            if (result.Errors.Count > 20)
                vm.Logger.Log($"⚠️ Дополнительно ошибок: {result.Errors.Count - 20}", LogLevel.Warning, "⚠️");
        }
        catch (OperationCanceledException)
        {
            vm.Logger.Log("⚠️ Индексация лиц отменена.", LogLevel.Warning, "⚠️");
        }
        catch (Exception ex)
        {
            vm.Logger.Log($"❌ Ошибка индексации лиц: {ex.Message}", LogLevel.Error, "❌");
        }
        finally
        {
            progressDialog.CancelRequested -= OnCancel;
            progressDialog.Close();
        }
    }

    private static string ResolveFaceApiHealthUrl()
    {
        var baseUrl = Environment.GetEnvironmentVariable("PHOTOSORTER_FACE_API_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = "http://localhost:5272";

        if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
            baseUrl += "/";

        return new Uri(new Uri(baseUrl), "health").ToString();
    }

    private static bool IsFaceDiagnosticLogMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return message.StartsWith("Hash:", StringComparison.Ordinal)
               || message.StartsWith("Existing faces", StringComparison.Ordinal)
               || message.StartsWith("Skip unchanged", StringComparison.Ordinal)
               || message.StartsWith("Reindex required", StringComparison.Ordinal)
               || message.StartsWith("Face API:", StringComparison.Ordinal)
               || message.StartsWith("Face indexing done:", StringComparison.Ordinal)
               || message.StartsWith("  ? Face #", StringComparison.Ordinal)
               || message.StartsWith("  -> Face #", StringComparison.Ordinal)
               || message.StartsWith("  ? Skip face", StringComparison.Ordinal)
               || message.StartsWith("Face indexing failed:", StringComparison.Ordinal);
    }

    private static async Task<bool> IsFaceApiAvailableAsync(string healthUrl)
    {
        var urlsToTry = new List<string> { healthUrl };
        if (healthUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase))
            urlsToTry.Add(healthUrl.Replace("localhost", "127.0.0.1", StringComparison.OrdinalIgnoreCase));

        foreach (var url in urlsToTry.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using var http = new System.Net.Http.HttpClient
                    {
                        Timeout = TimeSpan.FromSeconds(5)
                    };

                    var response = await http.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                        return true;
                }
                catch
                {
                    // retry
                }

                await Task.Delay(400);
            }
        }

        return false;
    }

    private void ManagePersons_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var window = new PersonsWindow(ServiceLocator.CreateFaceCatalogService(), ServiceLocator.CreateFacePersonManagementService())
            {
                Owner = this
            };

            window.ShowDialog();
        }
        catch (Exception ex)
        {
            if (DataContext is MainViewModel vm)
                vm.Logger.Log($"❌ Ошибка окна персон: {ex.Message}", LogLevel.Error, "❌");

            MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void LabelUnknownFaces_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        try
        {
            var catalog = ServiceLocator.CreateFaceCatalogService();
            var labeling = ServiceLocator.CreateFaceLabelingService();
            var unknown = await catalog.GetUnknownFacesAsync(1000);

            if (!string.IsNullOrWhiteSpace(vm.FaceIndexFolder))
            {
                unknown = unknown
                    .Where(x => x.PhotoAsset?.FilePath is string p &&
                                p.StartsWith(vm.FaceIndexFolder, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (unknown.Count == 0)
            {
                MessageBox.Show("Неизвестных лиц не найдено.", "Лица", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var thresholdSlider = this.FindName("ThresholdSlider") as Slider;
            var threshold = thresholdSlider?.Value ?? 0.55;
            var autoRecovered = await AutoAssignUnknownBeforeReviewAsync(catalog, unknown, threshold + 0.05);
            if (autoRecovered > 0)
            {
                vm.Logger.Log($"🤖 Автодоназначено из неизвестных: {autoRecovered}", LogLevel.Info, "🤖");
                unknown = await catalog.GetUnknownFacesAsync(1000);
                if (!string.IsNullOrWhiteSpace(vm.FaceIndexFolder))
                {
                    unknown = unknown
                        .Where(x => x.PhotoAsset?.FilePath is string p &&
                                    p.StartsWith(vm.FaceIndexFolder, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }
            }

            if (unknown.Count == 0)
            {
                MessageBox.Show("После автопроверки неизвестных лиц не осталось.", "Лица", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var window = new UnknownFacesWindow(unknown, labeling, ServiceLocator.CreateFaceClusteringService(), ServiceLocator.GetFaceCatalogService())
            {
                Owner = this
            };

            window.ShowDialog();
            vm.Logger.Log($"✅ Подтверждение завершено. Назначено лиц: {window.AssignedCount}", LogLevel.Info, "✅");
        }
        catch (Exception ex)
        {
            vm.Logger.Log($"❌ Ошибка подтверждения лиц: {ex.Message}", LogLevel.Error, "❌");
            MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static async Task<int> AutoAssignUnknownBeforeReviewAsync(IFaceCatalogService catalog, IReadOnlyList<DetectedFace> unknownFaces, double threshold)
    {
        var knownPersons = await catalog.GetPersonsAsync();
        if (knownPersons.Count == 0 || unknownFaces.Count == 0)
            return 0;

        var knownFaces = await catalog.GetConfirmedFacesWithEmbeddingsAsync();
        var centroids = BuildPersonCentroids(knownFaces);
        if (centroids.Count == 0)
            return 0;

        var assigned = 0;
        foreach (var face in unknownFaces)
        {
            var vectorBytes = face.FaceEmbedding?.Vector;
            if (vectorBytes is not { Length: > 0 })
                continue;

            var vector = DecodeEmbedding(vectorBytes);
            if (vector.Length == 0)
                continue;

            if (TryFindBestPerson(centroids, vector, threshold, out var personId, out _))
            {
                await catalog.AssignFaceToPersonAsync(face.Id, personId);
                assigned++;
            }
        }

        return assigned;
    }

    private static Dictionary<int, float[]> BuildPersonCentroids(IReadOnlyList<DetectedFace> knownFaces)
    {
        var byPerson = knownFaces
            .Where(f => f.ConfirmedPersonId != null && f.FaceEmbedding?.Vector is { Length: > 0 })
            .GroupBy(f => f.ConfirmedPersonId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(f => DecodeEmbedding(f.FaceEmbedding!.Vector)).Where(v => v.Length > 0).ToList());

        var result = new Dictionary<int, float[]>();
        foreach (var kv in byPerson)
        {
            if (kv.Value.Count == 0)
                continue;

            var dim = kv.Value.Max(v => v.Length);
            var sum = new double[dim];
            var count = 0;
            foreach (var vec in kv.Value)
            {
                var len = Math.Min(dim, vec.Length);
                for (var i = 0; i < len; i++)
                    sum[i] += vec[i];
                count++;
            }

            if (count == 0)
                continue;

            var centroid = new float[dim];
            for (var i = 0; i < dim; i++)
                centroid[i] = (float)(sum[i] / count);

            NormalizeInPlace(centroid);
            result[kv.Key] = centroid;
        }

        return result;
    }

    private static bool TryFindBestPerson(Dictionary<int, float[]> centroids, float[] candidate, double threshold, out int personId, out double distance)
    {
        personId = default;
        distance = 1.0;

        if (centroids.Count == 0 || candidate.Length == 0)
            return false;

        var normalized = candidate.ToArray();
        NormalizeInPlace(normalized);

        foreach (var kv in centroids)
        {
            var d = CosineDistance(kv.Value, normalized);
            if (d < distance)
            {
                distance = d;
                personId = kv.Key;
            }
        }

        return distance <= threshold;
    }

    private static float[] DecodeEmbedding(byte[] bytes)
    {
        if (bytes.Length < sizeof(float) || bytes.Length % sizeof(float) != 0)
            return [];

        var values = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }

    private static void NormalizeInPlace(float[] v)
    {
        double sum = 0;
        for (var i = 0; i < v.Length; i++)
            sum += v[i] * v[i];

        var norm = Math.Sqrt(sum);
        if (norm <= double.Epsilon)
            return;

        for (var i = 0; i < v.Length; i++)
            v[i] = (float)(v[i] / norm);
    }

    private static double CosineDistance(float[] a, float[] b)
    {
        var len = Math.Min(a.Length, b.Length);
        if (len == 0)
            return 1.0;

        double dot = 0;
        double na = 0;
        double nb = 0;

        for (var i = 0; i < len; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }

        if (na <= double.Epsilon || nb <= double.Epsilon)
            return 1.0;

        var cosine = dot / (Math.Sqrt(na) * Math.Sqrt(nb));
        return 1.0 - cosine;
    }

    #endregion

    #region Menu

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("PhotoSorter v2.0\nСортировка фото/видео/документов по дате, поиск дубликатов, очистка и переименование.", "О программе");
    }

    private void Formats_Click(object sender, RoutedEventArgs e)
    {
        var text = @"
Поддерживаемые форматы:

📸 Фото: JPG, JPEG, PNG, BMP, TIFF, CR2, CR3, NEF, ARW, DNG
🎥 Видео: MP4, MOV, AVI, MKV, WMV, M4V
📄 Документы: PDF, DOC/DOCX, XLS/XLSX, PPT/PPTX, TXT, RTF

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

    private void OpenWebGallery_Click(object sender, RoutedEventArgs e)
    {
        const string url = "http://localhost:5288";

        try
        {
            if (_webServerProcess is { HasExited: false })
            {
                try
                {
                    _webServerProcess.Kill(true);
                    _webServerProcess.Dispose();
                }
                catch
                {
                    // ignore restart cleanup errors
                }

                _webServerProcess = null;
            }

            var projectPath = FindWebProjectPath();
            if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
            {
                MessageBox.Show("Не найден проект веб-галереи PhotoSorterApp.Web.", "Веб-альбомы", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{projectPath}\" --urls {url}",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(projectPath) ?? AppContext.BaseDirectory
            };

            _webServerProcess = Process.Start(startInfo);
            if (DataContext is MainViewModel vm)
                vm.Logger.Log($"🌐 Запущен локальный веб-сервер: {url}", LogLevel.Info, "🌐");

            Task.Delay(1200).ContinueWith(_ =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch
                {
                    // ignore browser launch errors from background continuation
                }
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось открыть веб-галерею: {ex.Message}", "Веб-альбомы", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string? FindWebProjectPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "PhotoSorterApp.Web", "PhotoSorterApp.Web.csproj");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return null;
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
        h1 { color: #2563EB; border-bottom: 2px solid #eee; padding-bottom: 10px; }
        h2 { color: #333; margin-top: 30px; }
        h3 { color: #555; margin-top: 20px; }
        .section { margin-bottom: 30px; }
        code { background: #f5f5f5; padding: 2px 6px; border-radius: 4px; font-family: Consolas, monospace; }
        .tip { background: #e6f4ff; border-left: 4px solid #2563EB; padding: 15px; margin: 15px 0; }
        ul { padding-left: 20px; }
        li { margin-bottom: 8px; }
    </style>
</head>
<body>
    <h1>PhotoSorter — Полная справка (v2.0)</h1>

    <div class='section'>
        <h2>1. Общие принципы</h2>
        <p>PhotoSorter — это инструмент для безопасного управления цифровым архивом фотографий, видео и документов.</p>
        <div class='tip'>
            <strong>Важно:</strong> программа по умолчанию <strong>не удаляет файлы безвозвратно</strong>. Операции «Дубликаты» и «Очистка» перемещают файлы в папку карантина.
        </div>
        <ul>
            <li><strong>Сортировка</strong>: переносит файлы в структуру по дате (год/месяц).</li>
            <li><strong>Дубликаты</strong>: ищет одинаковые файлы по содержимому (хеш).</li>
            <li><strong>Очистка</strong>: ищет мусор (скриншоты/временные/пустые) и перемещает в карантин.</li>
            <li><strong>Переименование</strong>: переименовывает по шаблону с превью результата.</li>
            <li><strong>Каталог</strong>: генерирует HTML-каталог выбранной папки.</li>
            <li><strong>Лог</strong>: показывает историю операций, можно сохранить в файл.</li>
        </ul>
    </div>

    <div class='section'>
        <h2>2. Вкладка «Сортировка»</h2>
        <h3>Как работает</h3>
        <ul>
            <li>Выберите папку-источник.</li>
            <li>Выберите тип файлов: фото/видео/фото+видео/документы.</li>
            <li>Дата берётся из метаданных (EXIF), иначе — из даты создания файла.</li>
            <li>Структура: <code>Год/</code> или <code>Год/Месяц/</code> (если включено «Разбивать по месяцам»).</li>
        </ul>
        <h3>Опции</h3>
        <ul>
            <li><strong>Рекурсивный поиск</strong> — обрабатывать подпапки.</li>
            <li><strong>Разбивать по месяцам</strong> — год/месяц вместо только года.</li>
            <li><strong>Создать бэкап</strong> — создать резервную копию исходной папки перед сортировкой.</li>
        </ul>
    </div>

    <div class='section'>
        <h2>3. Вкладка «Дубликаты»</h2>
        <ul>
            <li>Выберите папку для поиска.</li>
            <li>Можно включить рекурсивный поиск.</li>
            <li>Выберите тип файлов (фото/видео/фото+видео/документы).</li>
            <li>Результат показывается группами; лишние файлы можно отправить в карантин.</li>
        </ul>
    </div>

    <div class='section'>
        <h2>4. Вкладка «Очистка»</h2>
        <ul>
            <li>Выберите папку для очистки.</li>
            <li>Опции: рекурсивно, скриншоты, временные файлы, пустые файлы.</li>
            <li>Найденные элементы перемещаются в подкаталог карантина внутри выбранной папки.</li>
        </ul>
    </div>

    <div class='section'>
        <h2>5. Вкладка «Переименование»</h2>
        <ul>
            <li>Выберите папку.</li>
            <li>Выберите шаблон или задайте свой.</li>
            <li>Доступные блоки: <code>{date}</code>, <code>{year}</code>, <code>{month}</code>, <code>{day}</code>, <code>{index}</code>, <code>{name}</code>.</li>
            <li>Показывается превью результата.</li>
        </ul>
    </div>

    <div class='section'>
        <h2>6. Вкладка «Каталог»</h2>
        <ul>
            <li>Генерирует HTML-каталог выбранной папки.</li>
            <li>Можно включить/выключить отображение файлов и размеров.</li>
            <li>Файл сохраняется как <code>catalog_YYYYMMDD_HHMMSS.html</code> в выбранной папке.</li>
        </ul>
    </div>

    <div class='section'>
        <h2>7. Вкладка «Лог»</h2>
        <ul>
            <li>Отображает сообщения о ходе операций.</li>
            <li>Можно сохранить лог в текстовый файл.</li>
        </ul>
    </div>

    <div class='section'>
        <h2>8. Вкладка «Лица»</h2>
        <ul>
            <li>Индексация лиц по папке с фото.</li>
            <li>Назначение имён неизвестным лицам из каталога.</li>
        </ul>
    </div>

    <hr>
    <p><em>PhotoSorter v2.0</em></p>
</body>
</html>";
    }

    #endregion

    #region Folder selection dialog (helper class)

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
                        "Нельзя выбирать корневой диск (C:\\, D:\\ и т.d.)!\n" +
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

    #region Database Diagnostics

    private async void ResetDatabase_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "⚠️ ВНИМАНИЕ: Это ПОЛНОСТЬЮ очистит БД индексированных фото и лиц.\n\n" +
            "После очистки вам нужно будет заново запустить индексацию.\n\n" +
            "Вы уверены?",
            "Подтверждение очистки БД",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            var vm = DataContext as MainViewModel;
            if (vm == null) return;

            vm.Logger.Log("🧹 Начало очистки БД...", LogLevel.Info, "🧹");

            var catalogService = ServiceLocator.CreateFaceCatalogService();
            var resetService = new FaceDatabaseResetService(catalogService);
            var resetResult = await resetService.ResetAllAsync();

            if (resetResult.Success)
            {
                vm.Logger.Log($"✅ Статистика:", LogLevel.Info, "📊");
                vm.Logger.Log($"   До: {resetResult.PhotosBefore} фото, {resetResult.FacesBefore} лиц, {resetResult.PersonsBefore} персон, {resetResult.TagsBefore} тегов", LogLevel.Info, "📊");
                vm.Logger.Log($"{resetResult.Message}", LogLevel.Info, "✅");
                MessageBox.Show(resetResult.Message, "БД очищена", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                vm.Logger.Log($"❌ {resetResult.Message}", LogLevel.Error, "❌");
                MessageBox.Show(resetResult.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            var vm = DataContext as MainViewModel;
            vm?.Logger.Log($"❌ Ошибка: {ex.Message}", LogLevel.Error, "❌");
            MessageBox.Show($"Ошибка очистки: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    private async void ResetFaceCatalog_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        var confirm = MessageBox.Show(
            "Полностью очистить базу лиц?\nБудут удалены все персоны, лица и подтверждения.",
            "Очистка базы лиц",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            var catalog = ServiceLocator.CreateFaceCatalogService();
            await catalog.ResetCatalogAsync();
            vm.Logger.Log("🧹 База лиц очищена.", LogLevel.Warning, "🧹");
            MessageBox.Show("База лиц очищена. Выполните индексацию заново.", "Лица", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            vm.Logger.Log($"❌ Ошибка очистки базы лиц: {ex.Message}", LogLevel.Error, "❌");
            MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}