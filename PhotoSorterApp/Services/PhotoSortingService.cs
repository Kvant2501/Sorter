#nullable enable
using PhotoSorterApp.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace PhotoSorterApp.Services;

/// <summary>
/// Service that sorts photos into folders by date taken.
/// </summary>
public class PhotoSortingService
{
    /// <summary>
    /// Default constructor.
    /// </summary>
    public PhotoSortingService()
    {
    }

    /// <summary>
    /// Sorts photos: moves files into Year[/Month] structure.
    /// Returns moved file count and any errors encountered.
    /// </summary>
    public (int MovedFiles, List<string> Errors) SortPhotos(
        SortingOptions options,
        FileTypeProfile profile,
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

        var errors = new List<string>();

        if (allFiles.Count == 0)
        {
            return (0, errors);
        }

        // Optionally create a backup of the source
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
                DateTime dateTaken = MetadataService.GetPhotoDateTaken(file) ?? File.GetCreationTime(file);
                string targetDir = options.SplitByMonth
                    ? Path.Combine(options.SourceFolder, dateTaken.ToString("yyyy"), dateTaken.ToString("MM"))
                    : Path.Combine(options.SourceFolder, dateTaken.ToString("yyyy"));

                Directory.CreateDirectory(targetDir);
                string destFile = Path.Combine(targetDir, Path.GetFileName(file));

                // Avoid overwrite — add suffix on conflict
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