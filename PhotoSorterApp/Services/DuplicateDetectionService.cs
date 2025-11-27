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
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            throw new ArgumentException("Путь не может быть пустым.", nameof(folderPath));

        var searchOption = isRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var allFiles = Directory.GetFiles(folderPath, "*.*", searchOption)
            .Where(f => {
                var ext = Path.GetExtension(f);
                return extensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
            })
            .OrderBy(f => f)
            .ToList();

        if (allFiles.Count == 0)
            return new List<DuplicateGroup>();

        var hashGroups = new Dictionary<string, List<string>>();

        foreach (var file in allFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

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

                // Отладка: выведи хеш и путь
                System.Diagnostics.Debug.WriteLine($"[Хеш] {hash} → {file}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Ошибка хеширования {file}: {ex.Message}");
            }
        }

        // Группы с 2+ файлами
        var duplicates = hashGroups
            .Where(g => g.Value.Count > 1)
            .Select(g => new DuplicateGroup(g.Value))
            .ToList();

        return duplicates;
    }
}