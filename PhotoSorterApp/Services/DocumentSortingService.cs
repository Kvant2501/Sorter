#nullable enable

using PhotoSorterApp.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace PhotoSorterApp.Services;

public class DocumentSortingService
{
    private readonly Action<string>? _logger;

    public DocumentSortingService(Action<string>? logger = null)
    {
        _logger = logger;
    }

    public (int MovedFiles, List<string> Errors) SortDocuments(
        SortingOptions options,
        IProgress<int>? progressPercent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.SourceFolder))
            throw new ArgumentException("Source folder is required.", nameof(options));

        _logger?.Invoke($"?? Начало сортировки документов: {options.SourceFolder}");

        var extensions = SupportedFormats.GetExtensionsByProfile(FileTypeProfile.DocumentsOnly);
        var extSet = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);

        var searchOption = options.IsRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        var allFiles = Directory.EnumerateFiles(options.SourceFolder, "*.*", searchOption)
            .Where(f => extSet.Contains(Path.GetExtension(f)))
            .Where(f => !IsInsideDocumentCategoryFolder(options.SourceFolder, f))
            .ToList();

        var errors = new List<string>();

        if (allFiles.Count == 0)
        {
            _logger?.Invoke("?? Документов для сортировки не найдено");
            return (0, errors);
        }

        _logger?.Invoke($"?? Найдено документов: {allFiles.Count}");

        if (options.CreateBackup)
        {
            _logger?.Invoke("?? Создание резервной копии...");
            var backupDir = Path.Combine(options.SourceFolder, $"Backup_{DateTime.Now:yyyyMMdd_HHmm}");
            try
            {
                CopyDirectory(options.SourceFolder, backupDir, excludeDirs: new[] { backupDir });
                _logger?.Invoke($"? Backup создан: {backupDir}");
            }
            catch (Exception ex)
            {
                var error = $"? Ошибка создания backup: {ex.Message}";
                errors.Add(error);
                _logger?.Invoke(error);
            }
        }

        int total = allFiles.Count;
        int processed = 0;
        int moved = 0;

        foreach (var file in allFiles)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger?.Invoke("?? Сортировка документов отменена пользователем");
                break;
            }

            try
            {
                var ext = Path.GetExtension(file);
                var extFolderName = string.IsNullOrWhiteSpace(ext)
                    ? "Other"
                    : ext.TrimStart('.').ToUpperInvariant();

                var date = GetDocumentDate(file);

                var targetDir = options.SplitByMonth
                    ? Path.Combine(options.SourceFolder, extFolderName, date.ToString("yyyy"), date.ToString("MM"))
                    : Path.Combine(options.SourceFolder, extFolderName, date.ToString("yyyy"));

                Directory.CreateDirectory(targetDir);

                var destFile = Path.Combine(targetDir, Path.GetFileName(file));
                if (File.Exists(destFile))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    int counter = 1;
                    do
                    {
                        destFile = Path.Combine(targetDir, $"{name}_{counter}{ext}");
                        counter++;
                    } while (File.Exists(destFile));
                }

                File.Move(file, destFile);
                moved++;
                _logger?.Invoke($"?? Перемещён: {Path.GetFileName(file)} ? {Path.GetRelativePath(options.SourceFolder, destFile)}");
            }
            catch (Exception ex)
            {
                var error = $"? Ошибка обработки {Path.GetFileName(file)}: {ex.Message}";
                errors.Add(error);
                _logger?.Invoke(error);
            }

            processed++;
            progressPercent?.Report((int)(100.0 * processed / total));
        }

        _logger?.Invoke($"? Сортировка документов завершена: перемещено {moved} из {total} файлов");
        return (moved, errors);
    }

    private static DateTime GetDocumentDate(string file)
    {
        try
        {
            var fi = new FileInfo(file);
            var creation = fi.CreationTime;
            var write = fi.LastWriteTime;

            var min = creation <= write ? creation : write;

            if (min.Year < 1970 || min.Year > DateTime.Now.Year + 1)
                return DateTime.Now;

            return min;
        }
        catch
        {
            return DateTime.Now;
        }
    }

    private static bool IsInsideDocumentCategoryFolder(string root, string filePath)
    {
        try
        {
            var relative = Path.GetRelativePath(root, filePath);
            var firstPart = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(firstPart))
                return false;

            return firstPart.Equals("PDF", StringComparison.OrdinalIgnoreCase)
                || firstPart.Equals("DOC", StringComparison.OrdinalIgnoreCase)
                || firstPart.Equals("DOCX", StringComparison.OrdinalIgnoreCase)
                || firstPart.Equals("XLS", StringComparison.OrdinalIgnoreCase)
                || firstPart.Equals("XLSX", StringComparison.OrdinalIgnoreCase)
                || firstPart.Equals("PPT", StringComparison.OrdinalIgnoreCase)
                || firstPart.Equals("PPTX", StringComparison.OrdinalIgnoreCase)
                || firstPart.Equals("TXT", StringComparison.OrdinalIgnoreCase)
                || firstPart.Equals("RTF", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
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
            if (excludeDirs.Any(excl => Path.GetFullPath(subDir).StartsWith(Path.GetFullPath(excl), StringComparison.OrdinalIgnoreCase)))
                continue;

            var dirName = Path.GetFileName(subDir);
            var destDir = Path.Combine(targetDir, dirName);
            CopyDirectory(subDir, destDir, excludeDirs);
        }
    }
}
