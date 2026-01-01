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
    public (int MovedFiles, List<string> Errors) SortDocuments(
        SortingOptions options,
        IProgress<int>? progressPercent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.SourceFolder))
            throw new ArgumentException("Source folder is required.", nameof(options));

        var extensions = SupportedFormats.GetExtensionsByProfile(FileTypeProfile.DocumentsOnly);
        var extSet = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);

        var searchOption = options.IsRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        var allFiles = Directory.EnumerateFiles(options.SourceFolder, "*.*", searchOption)
            .Where(f => extSet.Contains(Path.GetExtension(f)))
            // не трогаем уже отсортированные папки верхнего уровня (PDF/DOCX/...)
            .Where(f => !IsInsideDocumentCategoryFolder(options.SourceFolder, f))
            .ToList();

        var errors = new List<string>();

        if (allFiles.Count == 0)
            return (0, errors);

        if (options.CreateBackup)
        {
            var backupDir = Path.Combine(options.SourceFolder, $"Backup_{DateTime.Now:yyyyMMdd_HHmm}");
            try
            {
                CopyDirectory(options.SourceFolder, backupDir, excludeDirs: new[] { backupDir });
            }
            catch (Exception ex)
            {
                errors.Add($"Backup creation error: {ex.Message}");
            }
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
            }
            catch (Exception ex)
            {
                errors.Add($"Error processing {file}: {ex.Message}");
            }

            processed++;
            progressPercent?.Report((int)(100.0 * processed / total));
        }

        return (moved, errors);
    }

    private static DateTime GetDocumentDate(string file)
    {
        try
        {
            var fi = new FileInfo(file);
            var creation = fi.CreationTime;
            var write = fi.LastWriteTime;

            // Берём более раннюю (обычно ближе к фактической дате документа)
            var min = creation <= write ? creation : write;

            // страховка от "битых" дат
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

            // Сравниваем с ожидаемыми папками категорий
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
