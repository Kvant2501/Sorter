#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using PhotoSorterApp.Models;

namespace PhotoSorterApp.Services;

/// <summary>
/// Сервис поиска дубликатов в файловой системе по хешу SHA256.
/// </summary>
public class DuplicateDetectionService
{
    /// <summary>
    /// Находит группы дублей в указанной папке для заданных расширений.
    /// Возвращает список групп, каждая группа содержит пути к файлам с одинаковым хешем.
    /// </summary>
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

        // EnumerateFiles позволяет начать обработку сразу без выделения огромного списка
        var filesEnumerable = Directory.EnumerateFiles(folderPath, "*.*", searchOption)
            .Where(f => extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));

        // Попробуем посчитать общее количество для отображения прогресса, но это не критично
        int total = 0;
        try
        {
            total = filesEnumerable.Count();
        }
        catch
        {
            // На больших или сетевых хранилищах Count может падать — оставляем total = 0
            total = 0;
        }

        if (total == 0)
        {
            // Быстрый выход, если файлов нет
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