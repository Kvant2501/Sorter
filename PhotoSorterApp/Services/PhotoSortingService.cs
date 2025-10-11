using PhotoSorterApp.Models;
using PhotoSorterApp.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace PhotoSorterApp.Services;

public class PhotoSortingService
{
    private readonly LogCollection _logger;

    public PhotoSortingService(LogCollection logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public int SortPhotos(
        SortingOptions options,
        FileTypeProfile profile,
        IProgress<string> logProgress,
        IProgress<int> progressPercent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.SourceFolder))
            throw new ArgumentException("Source folder is required.", nameof(options));

        var extensions = SupportedFormats.GetExtensionsByProfile(profile);
        var extSet = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);

        var searchOption = options.IsRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var allFiles = Directory.GetFiles(options.SourceFolder, "*.*", searchOption)
            .Where(f => extSet.Contains(Path.GetExtension(f)))
            .ToList();

        if (allFiles.Count == 0)
        {
            logProgress?.Report("Файлы для сортировки не найдены.");
            return 0;
        }

        // Опционально: создать бэкап
        if (options.CreateBackup)
        {
            var backupDir = Path.Combine(options.SourceFolder, $"Backup_{DateTime.Now:yyyyMMdd_HHmm}");
            logProgress?.Report($"Создание бэкапа: {backupDir}");
            CopyDirectory(options.SourceFolder, backupDir, excludeDirs: new[] { backupDir });
        }

        int total = allFiles.Count;
        int processed = 0;
        int moved = 0;

        foreach (var file in allFiles)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                DateTime dateTaken = MetadataService.GetPhotoDateTaken(file) ?? File.GetCreationTime(file);
                string targetDir = options.SplitByMonth
                    ? Path.Combine(options.SourceFolder, dateTaken.ToString("yyyy"), dateTaken.ToString("MM"))
                    : Path.Combine(options.SourceFolder, dateTaken.ToString("yyyy"));

                Directory.CreateDirectory(targetDir);
                string destFile = Path.Combine(targetDir, Path.GetFileName(file));

                // Избегаем перезаписи
                if (File.Exists(destFile))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    string ext = Path.GetExtension(file);
                    int counter = 1;
                    do
                    {
                        destFile = Path.Combine(targetDir, $"{name}_{counter}{ext}");
                        counter++;
                    } while (File.Exists(destFile));
                }

                File.Move(file, destFile);
                moved++;
                logProgress?.Report($"✅ Перемещено: {Path.GetFileName(file)} → {Path.GetFileName(targetDir)}");
            }
            catch (Exception ex)
            {
                _logger.Log($"❌ Ошибка обработки {file}: {ex.Message}", LogLevel.Error);
            }

            processed++;
            progressPercent?.Report((int)(100.0 * processed / total));
        }

        return moved;
    }

    private static void CopyDirectory(string sourceDir, string targetDir, IEnumerable<string>? excludeDirs = null)
    {
        excludeDirs ??= Array.Empty<string>();
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            var destFile = Path.Combine(targetDir, fileName);
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(subDir);
            var destDir = Path.Combine(targetDir, dirName);

            if (excludeDirs.Any(excl => Path.GetFullPath(subDir).StartsWith(Path.GetFullPath(excl), StringComparison.OrdinalIgnoreCase)))
                continue;

            CopyDirectory(subDir, destDir, excludeDirs);
        }
    }
}