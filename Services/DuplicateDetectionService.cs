using PhotoSorterApp.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace PhotoSorterApp.Services;

public class DuplicateDetectionService
{
    public List<DuplicateGroup> FindDuplicatesWithExtensions(string folderPath, bool isRecursive, string[] extensions)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            throw new ArgumentException("Путь не может быть пустым.", nameof(folderPath));

        // Исправление CS1503: создаём HashSet правильно
        var extSet = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);

        var searchOption = isRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(folderPath, "*.*", searchOption)
            .Where(f => extSet.Contains(Path.GetExtension(f)))
            .ToList();

        var hashGroups = new Dictionary<string, List<string>>();

        foreach (var file in files)
        {
            try
            {
                using var stream = File.OpenRead(file);
                var hash = SHA256.HashData(stream);
                var hashStr = Convert.ToHexString(hash);

                if (!hashGroups.ContainsKey(hashStr))
                    hashGroups[hashStr] = new List<string>();
                hashGroups[hashStr].Add(file);
            }
            catch (Exception ex)
            {
                // Пропускаем файлы, которые нельзя прочитать
                continue;
            }
        }

        // Фильтруем группы с 2+ файлами
        var duplicates = hashGroups
            .Where(g => g.Value.Count > 1)
            .Select(g => new DuplicateGroup { Files = g.Value })
            .ToList();

        return duplicates;
    }
}