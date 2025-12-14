#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using PhotoSorterApp.Models;

namespace PhotoSorterApp.Services;

public class DuplicateDetectionService
{
    public List<DuplicateGroup> FindDuplicatesWithExtensions(
        string folderPath,
        bool isRecursive,
        HashSet<string> extensions,
        CancellationToken cancellationToken = default,
        IProgress<(int processed, int total, string? current)>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            throw new ArgumentException("Путь не может быть пустым.", nameof(folderPath));

        var searchOption = isRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        // Используем EnumerateFiles чтобы не выделять большой список и позволить отмену во время перечисления
        var filesEnumerable = Directory.EnumerateFiles(folderPath, "*.*", searchOption)
            .Where(f => extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));

        // Для отображения прогресса попробуем посчитать общее количество (это всё ещё лениво-итеративно)
        int total = 0;
        try
        {
            total = filesEnumerable.Count();
        }
        catch (Exception)
        {
            // На некоторых больших сетевых ресурсах Count() может падать — в этом случае оставим total = 0 и продолжим
            total = 0;
        }

        if (total == 0)
        {
            // Попытка быстрого выхода — если нет файлов с такими расширениями
            if (!filesEnumerable.Any())
                return new List<DuplicateGroup>();
        }

        var hashGroups = new Dictionary<string, List<string>>();
        int processedFiles = 0;

        try
        {
            foreach (var file in filesEnumerable)
            {
                cancellationToken.ThrowIfCancellationRequested();

                processedFiles++;
                progress?.Report((processedFiles, total, file));

                try
                {
                    byte[] hashBytes;
                    using (var stream = File.OpenRead(file))
                    using (var sha256 = SHA256.Create())
                    {
                        hashBytes = sha256.ComputeHash(stream);
                    }
                    var hash = Convert.ToBase64String(hashBytes);

                    if (!hashGroups.TryGetValue(hash, out var group))
                    {
                        group = new List<string>();
                        hashGroups[hash] = group;
                    }
                    group.Add(file);

                    System.Diagnostics.Debug.WriteLine($"[Хеш] {hash} → {file} ({processedFiles}/{(total > 0 ? total : processedFiles)})");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Ошибка хеширования {file}: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Поиск отменён после обработки {processedFiles} файлов{(total>0? $" из {total}": string.Empty)}");
            throw;
        }

        var duplicates = hashGroups
            .Where(g => g.Value.Count > 1)
            .Select(g => new DuplicateGroup(g.Value))
            .ToList();

        return duplicates;
    }
}