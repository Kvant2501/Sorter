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
using System.Text;

namespace PhotoSorterApp;

public partial class MainWindow : Window
{
    private CancellationTokenSource? _cts;

    private static string GetDockerComposeWorkingDirectory()
    {
        // Try several likely starting points and search upward for a 'docker/docker-compose.yml' folder
        var candidates = new[]
        {
            AppDomain.CurrentDomain.BaseDirectory,
            AppContext.BaseDirectory,
            Environment.CurrentDirectory,
            Path.GetDirectoryName(typeof(MainWindow).Assembly.Location) ?? string.Empty
        };

        foreach (var start in candidates.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => Path.GetFullPath(s)))
        {
            var dir = start.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                var candidate = Path.Combine(dir, "docker");
                var compose = Path.Combine(candidate, "docker-compose.yml");
                try
                {
                    if (File.Exists(compose))
                        return candidate;
                }
                catch { }

                var parent = Path.GetDirectoryName(dir);
                if (string.IsNullOrEmpty(parent) || parent == dir) break;
                dir = parent;
            }
        }

        // Fallback: docker folder next to application base directory (original behavior)
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docker");
    }

    private static string GetDockerComposeFilePath()
        => Path.Combine(GetDockerComposeWorkingDirectory(), "docker-compose.yml");

    private async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(string fileName, string arguments, string workingDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var p = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        p.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        await p.WaitForExitAsync();

        return (p.ExitCode, stdout.ToString().Trim(), stderr.ToString().Trim());
    }

    private async void StartGalleryDocker_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var wd = GetDockerComposeWorkingDirectory();
        var composeFile = GetDockerComposeFilePath();

        if (!File.Exists(composeFile))
        {
            vm.Logger.Log($"❌ Не найден docker-compose.yml: {composeFile}", LogLevel.Error, "❌");
            MessageBox.Show($"Не найден docker-compose.yml:\n{composeFile}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Require a gallery folder to be selected so we know what to mount
        if (string.IsNullOrWhiteSpace(vm.GalleryFolder))
        {
            MessageBox.Show(
                "Сначала выберите папку с фотографиями.\n\nЭта папка будет подключена к Docker-контейнеру.",
                "Папка не выбрана",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!Directory.Exists(vm.GalleryFolder))
        {
            MessageBox.Show($"Папка не существует: {vm.GalleryFolder}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Quick check: is Docker Engine reachable?
        try
        {
            var (pingCode, _, pingErr) = await RunProcessAsync("docker", "info", wd);
            if (pingCode != 0)
            {
                var msg = "Docker недоступен. Похоже, Docker Desktop (Engine) не запущен.\n\n" +
                          "Шаги:\n" +
                          "1) Запустите Docker Desktop\n" +
                          "2) Дождитесь статуса 'Engine running'\n" +
                          "3) Нажмите 'Запустить Docker' ещё раз\n\n" +
                          "Детали: " + (string.IsNullOrWhiteSpace(pingErr) ? "docker info вернул код " + pingCode : pingErr);

                vm.Logger.Log("❌ Docker: Engine недоступен (docker info).", LogLevel.Error, "❌");
                if (!string.IsNullOrWhiteSpace(pingErr))
                    vm.Logger.Log(pingErr, LogLevel.Error, "❌");

                MessageBox.Show(msg, "Docker", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        catch (Exception ex)
        {
            vm.Logger.Log($"❌ Docker: не удалось выполнить 'docker info': {ex.Message}", LogLevel.Error, "❌");
            MessageBox.Show(
                "Docker недоступен. Убедитесь, что Docker Desktop установлен и запущен.",
                "Docker",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Always set PHOTOS_DIR from the selected gallery folder so compose mounts it correctly.
        Environment.SetEnvironmentVariable("PHOTOS_DIR", vm.GalleryFolder);
        vm.Logger.Log($"🐳 Docker: PHOTOS_DIR = {vm.GalleryFolder}", LogLevel.Info, "🐳");

        // Use --force-recreate so Docker picks up the (possibly changed) PHOTOS_DIR volume mount.
        vm.Logger.Log("🐳 Docker: запускаю 'docker compose up -d --force-recreate'...", LogLevel.Info, "🐳");
        try
        {
            var (code, outText, errText) = await RunProcessAsync("docker", "compose up -d --force-recreate", wd);
            if (!string.IsNullOrWhiteSpace(outText)) vm.Logger.Log(outText, LogLevel.Info, "🐳");
            if (!string.IsNullOrWhiteSpace(errText)) vm.Logger.Log(errText, code == 0 ? LogLevel.Warning : LogLevel.Error, code == 0 ? "⚠️" : "❌");

            if (code != 0)
            {
                MessageBox.Show("Не удалось запустить Docker compose. Смотрите вкладку 'Лог'.", "Docker", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Refresh service status after start
            vm.Logger.Log("🐳 Docker: ожидание готовности сервиса...", LogLevel.Info, "🐳");
            await Task.Delay(3000);
            using var gallery = new GalleryService();
            var isAvailable = await gallery.IsAvailableAsync();
            vm.GalleryServiceStatus = isAvailable ? "✅ Gallery-сервис доступен" : "❌ Сервис недоступен";

            // Debug: check what's mounted at /photos inside the container
            if (isAvailable)
            {
                try
                {
                    var debugInfo = await gallery.DebugListFilesAsync("/photos");
                    if (!debugInfo.Exists)
                    {
                        vm.Logger.Log("⚠️ Docker: папка /photos НЕ существует внутри контейнера!", LogLevel.Warning, "⚠️");
                    }
                    else
                    {
                        vm.Logger.Log($"🐳 Docker: /photos смонтирована. Файлов: {debugInfo.Files.Length}, Папок: {debugInfo.Dirs.Length}", LogLevel.Info, "🐳");
                        if (debugInfo.Files.Length == 0 && debugInfo.Dirs.Length == 0)
                        {
                            vm.Logger.Log("⚠️ Docker: папка /photos пуста! Проверьте, что выбрана правильная папка с фотографиями.", LogLevel.Warning, "⚠️");
                        }
                        else
                        {
                            foreach (var dir in debugInfo.Dirs.Take(5))
                                vm.Logger.Log($"   📁 {dir}", LogLevel.Info, "🐳");
                            foreach (var file in debugInfo.Files.Take(5))
                                vm.Logger.Log($"   📄 {file.Name} ({file.Size / 1024} KB)", LogLevel.Info, "🐳");
                            if (debugInfo.TotalEntries > 10)
                                vm.Logger.Log($"   ... и ещё {debugInfo.TotalEntries - 10} записей", LogLevel.Info, "🐳");
                        }
                    }
                    if (debugInfo.Errors.Length > 0)
                    {
                        foreach (var err in debugInfo.Errors)
                            vm.Logger.Log($"❌ Docker debug: {err}", LogLevel.Error, "❌");
                    }
                }
                catch (Exception debugEx)
                {
                    vm.Logger.Log($"⚠️ Docker debug: не удалось проверить /photos: {debugEx.Message}", LogLevel.Warning, "⚠️");
                }
            }
        }
        catch (Exception ex)
        {
            vm.Logger.Log($"❌ Docker: ошибка запуска compose: {ex.Message}", LogLevel.Error, "❌");
            MessageBox.Show(ex.Message, "Docker", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void StopGalleryDocker_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var wd = GetDockerComposeWorkingDirectory();
        var composeFile = GetDockerComposeFilePath();
        if (!File.Exists(composeFile))
        {
            vm.Logger.Log($"❌ Не найден docker-compose.yml: {composeFile}", LogLevel.Error, "❌");
            return;
        }

        vm.Logger.Log("🐳 Docker: выполняю 'docker compose down --remove-orphans' (без удаления данных)...", LogLevel.Info, "🐳");
        try
        {
            var (code, outText, errText) = await RunProcessAsync("docker", "compose down --remove-orphans", wd);
            if (!string.IsNullOrWhiteSpace(outText)) vm.Logger.Log(outText, LogLevel.Info, "🐳");
            if (!string.IsNullOrWhiteSpace(errText)) vm.Logger.Log(errText, code == 0 ? LogLevel.Warning : LogLevel.Error, code == 0 ? "⚠️" : "❌");

            if (code != 0)
            {
                MessageBox.Show("Не удалось остановить Docker compose. Смотрите вкладку 'Лог'.", "Docker", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            vm.GalleryServiceStatus = "Не проверено";
            vm.GalleryStats = "";
            vm.GalleryIndexingStatus = "";
        }
        catch (Exception ex)
        {
            vm.Logger.Log($"❌ Docker: ошибка остановки compose: {ex.Message}", LogLevel.Error, "❌");
            MessageBox.Show(ex.Message, "Docker", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

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
            vm.Logger.Log($"⚠️ [{profileLabel}] Сортировка отменена пользователем.", LogLevel.Warning, "⚠️");
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
            vm.Logger.Log($"⚠️ [{profileLabel}] Сортировка отменена пользователем.", LogLevel.Warning, "⚠️");
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
            vm.Logger.Log("⚠️ [Документы] Сортировка отменена пользователем.", LogLevel.Warning, "⚠️");
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
            vm.Logger.Log("⚠️ [Документы] Сортировка отменена пользователем.", LogLevel.Warning, "⚠️");
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

        vm.Logger.Log($"🧹 Очистка: найдено кандидатов — {candidates.Count}.", LogLevel.Info, "🧹");
        foreach (var (path, reason) in candidates)
        {
            try
            {
                vm.Logger.Log($"   • {reason}: {path}", LogLevel.Info, "🧹");
            }
            catch { }
        }

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
            try
            {
                if (Directory.Exists(quarantineDir) && !Directory.EnumerateFileSystemEntries(quarantineDir).Any())
                    Directory.Delete(quarantineDir);
            }
            catch { }

            vm.Logger.Log($"🧹 Очистка: найдено файлов — {candidates.Count}, но переместить не удалось (ошибок: {failedCount}).", LogLevel.Warning, "🧹");
            MessageBox.Show(
                $"Найдено файлов: {candidates.Count}\nПеремещено: 0\nОшибок: {failedCount}\n\n" +
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

            var result = MessageBox.Show(
                $"HTML-каталог успешно создан!\n\n{catalogPath}\n\nОткрыть в браузере?",
                "Готово",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = catalogPath,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            vm.Logger.Log($"❌ Ошибка создания каталога: {ex.Message}", LogLevel.Error, "❌");
            MessageBox.Show($"Ошибка создания каталога:\n\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region Tab: AI Content Sorting

    private void SelectAiSourceFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() && DataContext is MainViewModel vm)
        {
            vm.AiSourceFolder = dialog.FolderName ?? string.Empty;
            vm.Logger.Log($"🤖 AI источник: {dialog.FolderName}", LogLevel.Info, "🤖");
        }
    }

    private void SelectAiOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() && DataContext is MainViewModel vm)
        {
            vm.AiOutputFolder = dialog.FolderName ?? string.Empty;
            vm.Logger.Log($"🤖 AI результат: {dialog.FolderName}", LogLevel.Info, "🤖");
        }
    }

    private async void CheckDocker_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        vm.AiDockerStatus = "Проверяю...";
        vm.Logger.Log("🐳 Проверка Docker CLIP-сервиса...", LogLevel.Info, "🐳");

        using var classifier = new ImageClassificationService();
        bool available = await classifier.IsAvailableAsync();

        if (available)
        {
            vm.AiDockerStatus = "✅ CLIP-сервис доступен";
            vm.Logger.Log("✅ Docker CLIP-сервис работает (http://localhost:8000)", LogLevel.Info, "✅");
        }
        else
        {
            vm.AiDockerStatus = "❌ Сервис недоступен";
            vm.Logger.Log("❌ Docker CLIP-сервис недоступен. Убедитесь, что контейнер запущен: docker compose up -d (из папки docker/)", LogLevel.Error, "❌");
        }
    }

    private async void StartAiSorting_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (string.IsNullOrWhiteSpace(vm.AiSourceFolder))
        {
            MessageBox.Show("Выберите папку-источник.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(vm.AiOutputFolder))
        {
            MessageBox.Show("Выберите папку для результата.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!Directory.Exists(vm.AiSourceFolder))
        {
            MessageBox.Show($"Папка не существует: {vm.AiSourceFolder}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        using var classifier = new ImageClassificationService();
        bool available = await classifier.IsAvailableAsync();
        if (!available)
        {
            MessageBox.Show(
                "CLIP-сервис недоступен!\n\nЗапустите Docker-контейнер:\n" +
                "1) Откройте терминал в папке docker/\n" +
                "2) Выполните: docker compose up -d\n" +
                "3) Дождитесь загрузки модели (~1 мин)\n" +
                "4) Нажмите «Проверить Docker»",
                "Docker не запущен",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        var progressDialog = new ProgressDialog("AI-сортировка", "Классификация изображений...");
        progressDialog.Owner = this;

        void OnCancel(object? s, EventArgs args)
        {
            _cts?.Cancel();
            vm.Logger.Log("⚠️ AI-сортировка отменена.", LogLevel.Warning, "⚠️");
        }

        progressDialog.CancelRequested += OnCancel;

        var progress = new Progress<(int processed, int total, string? current)>(t =>
        {
            try
            {
                var statusText = t.total > 0
                    ? $"AI: {t.processed}/{t.total}"
                    : $"AI: {t.processed}";
                progressDialog.UpdateStatus(statusText);

                if (!string.IsNullOrEmpty(t.current))
                    progressDialog.UpdateDetail(t.current);
            }
            catch { }
        });

        List<string>? categories = null;
        if (!string.IsNullOrWhiteSpace(vm.AiCategories))
        {
            categories = vm.AiCategories
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(c => c.Length > 0)
                .ToList();
        }

        int sorted = 0;
        var errors = new List<string>();

        try
        {
            progressDialog.Show();

            vm.Logger.Log($"🤖 AI-сортировка: {vm.AiSourceFolder} → {vm.AiOutputFolder}", LogLevel.Info, "🤖");

            await Task.Run(async () =>
            {
                var service = new ContentSortingService(classifier, msg =>
                {
                    vm.Logger.Log($"🤖 {msg}", LogLevel.Info, "🤖");
                });

                var result = await service.SortByContentAsync(
                    vm.AiSourceFolder,
                    vm.AiOutputFolder,
                    vm.IsAiRecursive,
                    categories,
                    vm.AiMinConfidence,
                    progress,
                    _cts!.Token);

                sorted = result.Sorted;
                errors = result.Errors;
            }, _cts.Token);

            if (!_cts.IsCancellationRequested)
            {
                vm.Logger.Log($"✅ AI-сортировка завершена. Рассортировано: {sorted}. Ошибок: {errors.Count}", LogLevel.Info, "✅");

                if (errors.Count > 0)
                {
                    foreach (var error in errors)
                        vm.Logger.Log(error, LogLevel.Error, "❌");
                }

                MessageBox.Show(
                    $"AI-сортировка завершена!\n\nРассортировано файлов: {sorted}\nОшибок: {errors.Count}\n\nРезультат: {vm.AiOutputFolder}",
                    "Готово",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (OperationCanceledException)
        {
            vm.Logger.Log("⚠️ AI-сортировка отменена.", LogLevel.Warning, "⚠️");
        }
        catch (Exception ex)
        {
            vm.Logger.Log($"❌ Критическая ошибка AI-сортировки: {ex.Message}", LogLevel.Error, "❌");
            MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            progressDialog.CancelRequested -= OnCancel;
            progressDialog.Close();
        }
    }

    #endregion

    #region Tab: Gallery

    private void SelectGalleryFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() && DataContext is MainViewModel vm)
        {
            vm.GalleryFolder = dialog.FolderName ?? string.Empty;
            vm.Logger.Log($"🌐 Галерея — выбрана папка: {dialog.FolderName}", LogLevel.Info, "🌐");
        }
    }

    private async void CheckGalleryService_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        vm.GalleryServiceStatus = "Проверяю...";
        vm.Logger.Log("🌐 Проверка gallery-сервиса...", LogLevel.Info, "🌐");

        using var gallery = new GalleryService();
        bool available = await gallery.IsAvailableAsync();

        if (available)
        {
            vm.GalleryServiceStatus = "✅ Gallery-сервис доступен";
            vm.Logger.Log("✅ Gallery-сервис работает (http://localhost:8080)", LogLevel.Info, "✅");

            try
            {
                var stats = await gallery.GetStatsAsync();
                vm.GalleryStats = $"📸 Фото: {stats.TotalPhotos}  |  📁 Альбомов: {stats.TotalAlbums}  |  📅 Лет: {stats.Years.Length}";
            }
            catch { }
        }
        else
        {
            vm.GalleryServiceStatus = "❌ Сервис недоступен";
            vm.Logger.Log("❌ Gallery-сервис недоступен. Запустите: docker compose up -d (из папки docker/)", LogLevel.Error, "❌");
        }
    }

    private int _currentGalleryTaskId;

    private async void StartGalleryIndexing_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (string.IsNullOrWhiteSpace(vm.GalleryFolder))
        {
            MessageBox.Show("Выберите папку с фотографиями.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var photosDirEnv = Environment.GetEnvironmentVariable("PHOTOS_DIR");
        string dockerPath;
        if (!string.IsNullOrWhiteSpace(photosDirEnv))
        {
            try
            {
                var selectedFull = Path.GetFullPath(vm.GalleryFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var photosFull = Path.GetFullPath(photosDirEnv).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (selectedFull.Equals(photosFull, StringComparison.OrdinalIgnoreCase))
                {
                    dockerPath = "/photos";
                }
                else if (selectedFull.StartsWith(photosFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    var rel = selectedFull.Substring(photosFull.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    dockerPath = "/photos/" + rel.Replace('\\', '/');
                }
                else
                {
                    dockerPath = "/photos";
                    vm.Logger.Log($"⚠️ Выбранная папка не внутри PHOTOS_DIR ({photosDirEnv}). Будет проиндексирован root /photos.", LogLevel.Warning, "⚠️");
                }
            }
            catch
            {
                dockerPath = "/photos";
            }
        }
        else
        {
            dockerPath = "/photos";
        }

        using var gallery = new GalleryService();
        bool available = await gallery.IsAvailableAsync();
        if (!available)
        {
            MessageBox.Show(
                "Gallery-сервис недоступен!\n\nЗапустите Docker:\n" +
                "1) Откройте терминал в папке docker/\n" +
                "2) Выполните: docker compose up -d\n" +
                "3) Дождитесь запуска (~30 сек)\n" +
                "4) Нажмите «Проверить сервис»",
                "Docker не запущен",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        vm.Logger.Log($"🌐 Запуск индексации: {vm.GalleryFolder} (в Docker: {dockerPath})", LogLevel.Info, "🌐");
        vm.GalleryIndexingStatus = "Запуск индексации...";

        try
        {
            var result = await gallery.StartIndexingAsync(dockerPath, vm.GalleryRecursive, vm.GalleryUseAi);
            _currentGalleryTaskId = result.TaskId;
            vm.GalleryIndexingStatus = $"Индексация запущена (задача #{result.TaskId})";
            vm.Logger.Log($"🌐 Индексация запущена, задача #{result.TaskId}", LogLevel.Info, "🌐");

            _ = PollGalleryIndexingStatus(vm, result.TaskId);
        }
        catch (Exception ex)
        {
            vm.GalleryIndexingStatus = $"Ошибка: {ex.Message}";
            vm.Logger.Log($"❌ Ошибка запуска индексации: {ex.Message}", LogLevel.Error, "❌");
        }
    }

    private async Task PollGalleryIndexingStatus(MainViewModel vm, int taskId)
    {
        using var gallery = new GalleryService();
        try
        {
            while (true)
            {
                await Task.Delay(2000);
                var status = await gallery.GetIndexingStatusAsync(taskId);

                string progressText = status.TotalFiles > 0
                    ? $"{status.ProcessedFiles}/{status.TotalFiles}"
                    : $"{status.ProcessedFiles} файлов";

                vm.GalleryIndexingStatus = status.Status switch
                {
                    "running" => $"⏳ Индексация: {progressText}",
                    "completed" => $"✅ Индексация завершена: {progressText}",
                    "error" => $"❌ Ошибка: {status.Error}",
                    _ => $"Статус: {status.Status}"
                };

                if (status.Status is "completed" or "error")
                {
                    if (status.Status == "completed")
                    {
                        vm.Logger.Log($"✅ Индексация завершена: {status.ProcessedFiles} файлов", LogLevel.Info, "✅");

                        try
                        {
                            var stats = await gallery.GetStatsAsync();
                            vm.GalleryStats = $"📸 Фото: {stats.TotalPhotos}  |  📁 Альбомов: {stats.TotalAlbums}  |  📅 Лет: {stats.Years.Length}";
                        }
                        catch { }
                    }
                    else
                    {
                        vm.Logger.Log($"❌ Индексация завершилась с ошибкой: {status.Error}", LogLevel.Error, "❌");
                    }
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            vm.GalleryIndexingStatus = $"Ошибка опроса: {ex.Message}";
        }
    }

    private async void RefreshGalleryStatus_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        using var gallery = new GalleryService();
        try
        {
            bool available = await gallery.IsAvailableAsync();
            if (!available)
            {
                vm.GalleryServiceStatus = "❌ Сервис недоступен";
                vm.GalleryStats = "";
                return;
            }

            vm.GalleryServiceStatus = "✅ Gallery-сервис доступен";

            var stats = await gallery.GetStatsAsync();
            vm.GalleryStats = $"📸 Фото: {stats.TotalPhotos}  |  📁 Альбомов: {stats.TotalAlbums}  |  📅 Лет: {stats.Years.Length}";

            if (_currentGalleryTaskId > 0)
            {
                var status = await gallery.GetIndexingStatusAsync(_currentGalleryTaskId);
                string progressText = status.TotalFiles > 0
                    ? $"{status.ProcessedFiles}/{status.TotalFiles}"
                    : $"{status.ProcessedFiles} файлов";

                vm.GalleryIndexingStatus = status.Status switch
                {
                    "running" => $"⏳ Индексация: {progressText}",
                    "completed" => $"✅ Индексация завершена: {progressText}",
                    "error" => $"❌ Ошибка: {status.Error}",
                    _ => $"Статус: {status.Status}"
                };
            }
        }
        catch (Exception ex)
        {
            vm.GalleryStats = $"Ошибка: {ex.Message}";
        }
    }

    private void OpenGallery_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "http://localhost:8080",
                UseShellExecute = true
            });

            if (DataContext is MainViewModel vm)
                vm.Logger.Log("🌐 Открыта галерея в браузере: http://localhost:8080", LogLevel.Info, "🌐");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось открыть браузер: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
        <h2>2–9. Описания вкладок</h2>
        <p>Подробное описание каждой вкладки доступно в интерфейсе приложения.</p>
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