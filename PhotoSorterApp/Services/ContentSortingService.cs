#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhotoSorterApp.Models;

namespace PhotoSorterApp.Services;

/// <summary>
/// Сервис сортировки изображений по контенту (через CLIP-классификацию в Docker).
/// </summary>
public class ContentSortingService
{
    private readonly ImageClassificationService _classifier;
    private readonly Action<string>? _logger;

    public ContentSortingService(ImageClassificationService classifier, Action<string>? logger = null)
    {
        _classifier = classifier;
        _logger = logger;
    }

    /// <summary>
    /// Сортирует изображения из sourceFolder по подпапкам на основе AI-классификации.
    /// </summary>
    public async Task<(int Sorted, List<string> Errors)> SortByContentAsync(
        string sourceFolder,
        string outputFolder,
        bool isRecursive,
        List<string>? customCategories,
        double minConfidence,
        IProgress<(int processed, int total, string? current)>? progress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceFolder))
            throw new ArgumentException("Папка-источник не указана.", nameof(sourceFolder));

        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".tiff", ".webp"
        };

        var searchOption = isRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.EnumerateFiles(sourceFolder, "*.*", searchOption)
            .Where(f => extensions.Contains(Path.GetExtension(f)))
            .ToList();

        if (files.Count == 0)
        {
            _logger?.Invoke("?? Изображений не найдено.");
            return (0, new List<string>());
        }

        _logger?.Invoke($"?? Найдено изображений: {files.Count}");

        var errors = new List<string>();
        int sorted = 0;
        int processed = 0;
        int total = files.Count;

        // Use custom categories or let the service use defaults (null)
        var categories = customCategories is { Count: > 0 } ? customCategories : null;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            processed++;
            progress?.Report((processed, total, Path.GetFileName(file)));

            try
            {
                var result = await _classifier.ClassifyAsync(file, categories, ct);

                if (result.Confidence < minConfidence)
                {
                    // Low confidence — move to "Неопределённые"
                    var uncertainDir = Path.Combine(outputFolder, "Неопределённые");
                    Directory.CreateDirectory(uncertainDir);
                    MoveFileSafe(file, uncertainDir);
                    sorted++;
                    _logger?.Invoke($"? {Path.GetFileName(file)} ? Неопределённые (confidence: {result.Confidence:P0})");
                    continue;
                }

                // Sanitize category name for folder
                var folderName = SanitizeFolderName(result.Best);
                var targetDir = Path.Combine(outputFolder, folderName);
                Directory.CreateDirectory(targetDir);

                MoveFileSafe(file, targetDir);
                sorted++;
                _logger?.Invoke($"?? {Path.GetFileName(file)} ? {folderName} ({result.Confidence:P0})");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var error = $"? Ошибка классификации {Path.GetFileName(file)}: {ex.Message}";
                errors.Add(error);
                _logger?.Invoke(error);
            }
        }

        _logger?.Invoke($"? AI-сортировка завершена: {sorted} из {total} файлов.");
        return (sorted, errors);
    }

    private static void MoveFileSafe(string sourceFile, string targetDir)
    {
        var destFile = Path.Combine(targetDir, Path.GetFileName(sourceFile));

        if (File.Exists(destFile))
        {
            var name = Path.GetFileNameWithoutExtension(sourceFile);
            var ext = Path.GetExtension(sourceFile);
            int counter = 1;
            do
            {
                destFile = Path.Combine(targetDir, $"{name}_{counter}{ext}");
                counter++;
            } while (File.Exists(destFile));
        }

        File.Move(sourceFile, destFile);
    }

    private static string SanitizeFolderName(string name)
    {
        // Replace invalid path characters
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());

        // Trim and capitalize first letter
        sanitized = sanitized.Trim();
        if (sanitized.Length > 0)
            sanitized = char.ToUpper(sanitized[0]) + sanitized[1..];

        return string.IsNullOrWhiteSpace(sanitized) ? "Другое" : sanitized;
    }
}
