#nullable enable

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PhotoSorterApp.Tests;

/// <summary>
/// Ёвристические тесты на "лишний" код.
/// ¬ажно: тесты не могут гарантировать 100% точность (reflection/DI/XAML св€зывание),
/// поэтому они помечены как Explicit и запускаютс€ только вручную.
/// </summary>
[TestFixture]
public class UnusedCodeHeuristicsTests
{
    private static string RepoRoot
    {
        get
        {
            // .../PhotoSorterApp.Tests/bin/Release/net8.0-windows/
            var dir = TestContext.CurrentContext.TestDirectory;
            // go up until we find *.sln or the app csproj
            var d = new DirectoryInfo(dir);
            while (d != null)
            {
                if (d.GetFiles("*.sln").Any() || d.GetFiles("PhotoSorterApp.csproj").Any())
                    return d.FullName;
                d = d.Parent;
            }
            return Directory.GetCurrentDirectory();
        }
    }

    [Test, Explicit("Ёвристика: запускаетс€ вручную перед рефакторингом/релизом")]
    public void MainWindow_ShouldNotContain_ObviousLegacyCompatibilityMethods()
    {
        var mainWindowPath = Path.Combine(RepoRoot, "PhotoSorterApp", "MainWindow.xaml.cs");
        Assert.That(File.Exists(mainWindowPath), Is.True, $"File not found: {mainWindowPath}");

        var text = File.ReadAllText(mainWindowPath);

        // ћетоды известны как "kept for compatibility" и дублируют текущую реализацию.
        // Ётот тест не удал€ет код, а сигнализирует, что пора прин€ть решение (удалить/оставить).
        StringAssert.Contains("SIMPLIFIED METHOD (kept for compatibility)", text,
            "ќжидалась пометка legacy-метода; если уже удалено Ч поправьте/удалите этот тест.");
    }

    [Test, Explicit("Ёвристика: запускаетс€ вручную. ћожет давать ложные срабатывани€")]
    public void Report_PossiblyUnusedPrivateMethods_InCsFiles()
    {
        var csFiles = Directory.EnumerateFiles(Path.Combine(RepoRoot, "PhotoSorterApp"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .ToList();

        // ќграниченно парсим private-методы: "private <ret> Name(".
        var rx = new Regex(@"\bprivate\s+(?:async\s+)?(?:[\w<>\[\]\?,]+\s+)+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled);

        var allText = string.Join("\n", csFiles.Select(File.ReadAllText));

        var candidates = new List<(string file, string name)>();
        foreach (var f in csFiles)
        {
            var t = File.ReadAllText(f);
            foreach (Match m in rx.Matches(t))
            {
                var name = m.Groups["name"].Value;
                // пропускаем конструкторы и стандартные обработчики событий
                if (string.Equals(name, Path.GetFileNameWithoutExtension(f), StringComparison.Ordinal))
                    continue;
                candidates.Add((f, name));
            }
        }

        // »щем "использование" как "name(" или "name <" и т.п. Ёто очень грубо.
        var possiblyUnused = candidates
            .Distinct()
            .Where(c => Regex.Matches(allText, $@"\b{Regex.Escape(c.name)}\b").Count < 2)
            .ToList();

        TestContext.WriteLine("=== Possibly unused private methods (heuristic) ===");
        foreach (var x in possiblyUnused)
            TestContext.WriteLine($"{Path.GetRelativePath(RepoRoot, x.file)} :: {x.name}");

        // Ќе падаем: это отчЄтный тест.
        Assert.Pass("—писок выведен в output теста (heuristic)." );
    }
}
