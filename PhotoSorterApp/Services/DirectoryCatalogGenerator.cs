using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PhotoSorterApp.Services;

public static class DirectoryCatalogGenerator
{
    public static string GenerateCatalog(string rootPath, bool includeFiles = true, bool includeSize = true)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang='ru'>");
        sb.AppendLine("<head>");
        sb.AppendLine("    <meta charset='UTF-8'>");
        sb.AppendLine("    <meta name='viewport' content='width=device-width, initial-scale=1.0'>");
        sb.AppendLine($"    <title>Каталог: {Path.GetFileName(rootPath)}</title>");
        sb.AppendLine("    <style>");
        sb.AppendLine(GetStyles());
        sb.AppendLine("    </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        
        sb.AppendLine("    <div class='container'>");
        sb.AppendLine("        <header>");
        sb.AppendLine("            <h1>Каталог файлов</h1>");
        sb.AppendLine($"            <p class='path'>{rootPath}</p>");
        sb.AppendLine($"            <p class='date'>Создано: {DateTime.Now:dd.MM.yyyy HH:mm:ss}</p>");
        sb.AppendLine("        </header>");
        
        // Statistics
        var stats = GetDirectoryStats(rootPath);
        sb.AppendLine("        <section class='stats'>");
        sb.AppendLine("            <div class='stats-grid'>");
        sb.AppendLine("                <div class='stat-card'>");
        sb.AppendLine($"                    <div class='stat-value'>{stats.TotalFiles}</div>");
        sb.AppendLine("                    <div class='stat-label'>Файлов</div>");
        sb.AppendLine("                </div>");
        sb.AppendLine("                <div class='stat-card'>");
        sb.AppendLine($"                    <div class='stat-value'>{stats.TotalFolders}</div>");
        sb.AppendLine("                    <div class='stat-label'>Папок</div>");
        sb.AppendLine("                </div>");
        sb.AppendLine("                <div class='stat-card'>");
        sb.AppendLine($"                    <div class='stat-value'>{FormatSize(stats.TotalSize)}</div>");
        sb.AppendLine("                    <div class='stat-label'>Общий размер</div>");
        sb.AppendLine("                </div>");
        sb.AppendLine("            </div>");
        sb.AppendLine("        </section>");
        
        // Directory tree
        sb.AppendLine("        <section class='tree-section'>");
        sb.AppendLine("            <h2>Структура папок</h2>");
        sb.AppendLine("            <div class='tree'>");
        
        var dirInfo = new DirectoryInfo(rootPath);
        sb.AppendLine($"            <div class='folder' data-path='{EscapeHtml(rootPath)}'>");
        sb.AppendLine($"                <span class='folder-icon' onclick='toggleFolder(this)'>[+]</span>");
        sb.AppendLine($"                <span class='folder-name'>{EscapeHtml(dirInfo.Name)}</span>");
        sb.AppendLine("                <div class='folder-content'>");
        BuildDirectoryContent(sb, rootPath, includeFiles, includeSize, 5);
        sb.AppendLine("                </div>");
        sb.AppendLine("            </div>");
        
        sb.AppendLine("            </div>");
        sb.AppendLine("        </section>");
        
        sb.AppendLine("        <footer>");
        sb.AppendLine("            <p>Создано с помощью PhotoSorter v1.0.2</p>");
        sb.AppendLine("        </footer>");
        sb.AppendLine("    </div>");
        
        sb.AppendLine("    <script>");
        sb.AppendLine(GetJavaScript());
        sb.AppendLine("    </script>");
        
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        
        return sb.ToString();
    }

    private static void BuildDirectoryContent(StringBuilder sb, string path, bool includeFiles, bool includeSize, int indentLevel)
    {
        var indent = new string(' ', indentLevel * 4);
        
        try
        {
            // Files first
            if (includeFiles)
            {
                var files = Directory.GetFiles(path).OrderBy(f => Path.GetFileName(f)).ToList();
                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file);
                    var fileIcon = GetFileIcon(file);
                    var fileSize = includeSize ? $" <span class='file-size'>({FormatSize(new FileInfo(file).Length)})</span>" : "";
                    var fileLink = $"file:///{file.Replace("\\", "/")}";
                    
                    sb.AppendLine($"{indent}    <div class='file'>");
                    sb.AppendLine($"{indent}        <span class='file-icon'>{fileIcon}</span>");
                    sb.AppendLine($"{indent}        <a href='{fileLink}' class='file-link' title='Открыть файл'>{EscapeHtml(fileName)}</a>");
                    sb.AppendLine($"{indent}        {fileSize}");
                    sb.AppendLine($"{indent}    </div>");
                }
            }

            // Then subdirectories (RECURSIVELY)
            var subDirs = Directory.GetDirectories(path).OrderBy(d => Path.GetFileName(d)).ToList();
            foreach (var subDir in subDirs)
            {
                var subDirInfo = new DirectoryInfo(subDir);
                var subDirName = subDirInfo.Name;

                int fileCount = 0;
                int subDirCount = 0;

                try
                {
                    if (includeFiles)
                        fileCount = Directory.GetFiles(subDir).Length;
                    subDirCount = Directory.GetDirectories(subDir).Length;
                }
                catch
                {
                    // Access denied - still show the folder
                }

                bool hasContent = fileCount > 0 || subDirCount > 0;

                string countInfo = "";
                if (hasContent)
                {
                    var parts = new List<string>();
                    if (includeFiles && fileCount > 0)
                        parts.Add($"{fileCount} файл(ов)");
                    if (subDirCount > 0)
                        parts.Add($"{subDirCount} папок");
                    countInfo = $" <span class='folder-count'>({string.Join(", ", parts)})</span>";
                }

                sb.AppendLine($"{indent}    <div class='folder' data-path='{EscapeHtml(subDir)}'>");
                sb.AppendLine($"{indent}        <span class='folder-icon' onclick='toggleFolder(this)'>[+]</span>");
                sb.AppendLine($"{indent}        <span class='folder-name'>{EscapeHtml(subDirName)}</span>{countInfo}");

                if (hasContent)
                {
                    sb.AppendLine($"{indent}        <div class='folder-content' style='display: none;'>");
                    BuildDirectoryContent(sb, subDir, includeFiles, includeSize, indentLevel + 3);
                    sb.AppendLine($"{indent}        </div>");
                }

                sb.AppendLine($"{indent}    </div>");
            }
        }
        catch (UnauthorizedAccessException)
        {
            sb.AppendLine($"{indent}    <div class='error'>?? Доступ запрещён</div>");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"{indent}    <div class='error'>?? Ошибка: {EscapeHtml(ex.Message)}</div>");
        }
    }

    private static DirectoryStats GetDirectoryStats(string path)
    {
        var stats = new DirectoryStats();
        
        try
        {
            var allFiles = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
            stats.TotalFiles = allFiles.Length;
            stats.TotalSize = allFiles.Sum(f => new FileInfo(f).Length);
            stats.TotalFolders = Directory.GetDirectories(path, "*", SearchOption.AllDirectories).Length;
        }
        catch
        {
            // Ignore errors
        }
        
        return stats;
    }

    private static string GetFileIcon(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".tiff" or ".webp" => "IMG",
            ".mp4" or ".mov" or ".avi" or ".mkv" or ".wmv" => "VID",
            ".pdf" => "PDF",
            ".doc" or ".docx" => "DOC",
            ".xls" or ".xlsx" => "XLS",
            ".ppt" or ".pptx" => "PPT",
            ".txt" or ".md" => "TXT",
            ".zip" or ".rar" or ".7z" => "ZIP",
            ".exe" or ".msi" => "EXE",
            ".mp3" or ".wav" or ".flac" or ".m4a" => "AUD",
            ".html" or ".htm" => "HTML",
            ".json" or ".xml" => "DATA",
            ".cs" or ".cpp" or ".py" or ".js" => "CODE",
            _ => "FILE"
        };
    }

    private static string FormatSize(long bytes)
    {
        string[] sizes = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private static string EscapeHtml(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");
    }

    private static string GetStyles()
    {
        return @"
        * {
            margin: 0;
            padding: 0;
            box-sizing: border-box;
        }
        
        :root {
            --fg: #1f2328;
            --muted: #57606a;
            --border: #d0d7de;
            --bg: #ffffff;
            --canvas: #f6f8fa;
            --accent: #0969da;
            --accent-bg: rgba(9,105,218,0.08);
            --radius: 10px;
        }

        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', 'Noto Sans', Helvetica, Arial, sans-serif, 'Apple Color Emoji', 'Segoe UI Emoji';
            background: var(--canvas);
            color: var(--fg);
            line-height: 1.5;
            padding: 20px;
        }
        
        .container {
            max-width: 1200px;
            margin: 0 auto;
            background: var(--bg);
            border-radius: var(--radius);
            border: 1px solid var(--border);
            overflow: hidden;
        }
        
        header {
            background: var(--bg);
            color: var(--fg);
            padding: 16px 20px;
            border-bottom: 1px solid var(--border);
        }

        header h1 {
            font-size: 1.25rem;
            font-weight: 600;
            margin: 0 0 4px 0;
        }

        .path {
            font-size: 0.9rem;
            color: var(--muted);
            word-break: break-all;
            margin: 0 0 2px 0;
        }

        .date {
            color: var(--muted);
            font-size: 0.8rem;
            margin: 0;
        }
        
        .stats {
            padding: 14px 20px;
            border-bottom: 1px solid var(--border);
            background: var(--bg);
        }
        
        .stats-grid {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
            gap: 12px;
        }
        
        .stat-card {
            background: var(--canvas);
            padding: 10px 12px;
            border-radius: 8px;
            text-align: left;
            border: 1px solid var(--border);
        }
        
        .stat-value {
            font-size: 1.1rem;
            font-weight: 600;
            color: var(--fg);
            margin-bottom: 2px;
        }
        
        .stat-label {
            color: var(--muted);
            font-size: 0.8rem;
        }
        
        .tree-section {
            padding: 18px 20px;
        }
        
        .tree-section h2 {
            color: var(--fg);
            margin-bottom: 12px;
            font-size: 1rem;
            font-weight: 600;
        }
        
        .tree {
            font-family: ui-monospace, SFMono-Regular, SFMono, Menlo, Consolas, 'Liberation Mono', monospace;
            background: var(--canvas);
            padding: 14px;
            border-radius: 8px;
            border: 1px solid var(--border);
        }
        
        .folder {
            margin: 4px 0;
            padding-left: 14px;
        }
        
        .folder-icon {
            cursor: pointer;
            user-select: none;
            margin-right: 6px;
            display: inline-block;
            width: 24px;
            color: var(--muted);
        }
        
        .folder-name {
            font-weight: 600;
            color: var(--accent);
        }
        
        .folder-count {
            color: var(--muted);
            font-size: 0.85em;
            font-weight: normal;
            margin-left: 8px;
        }
        
        .folder-content {
            margin-left: 18px;
            border-left: 2px solid var(--border);
            padding-left: 10px;
        }

        .file {
            margin: 2px 0;
            padding: 4px 6px;
            border-radius: 6px;
            transition: background 0.15s ease;
        }
        
        .file:hover {
            background: var(--accent-bg);
        }
        
        .file-icon {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            min-width: 44px;
            height: 18px;
            padding: 0 6px;
            border-radius: 999px;
            border: 1px solid var(--border);
            background: var(--bg);
            color: var(--muted);
            font-size: 0.72rem;
            line-height: 1;
            margin-right: 8px;
        }
        
        .file-link {
            color: var(--fg);
            text-decoration: none;
        }
        
        .file-link:hover {
            color: var(--accent);
            text-decoration: underline;
        }
        
        .file-size {
            color: var(--muted);
            font-size: 0.8rem;
            margin-left: 10px;
        }
        
        .error {
            color: #cf222e;
            font-style: italic;
            margin: 5px 0;
        }
        
        footer {
            text-align: center;
            padding: 14px 20px;
            color: var(--muted);
            background: var(--bg);
            border-top: 1px solid var(--border);
            font-size: 0.85rem;
        }
        
        @media print {
            body {
                background: white;
            }
            .container {
                box-shadow: none;
            }
        }";
    }

    private static string GetJavaScript()
    {
        return @"
        function toggleFolder(icon) {
            const folderDiv = icon.closest('.folder');
            if (!folderDiv) return;

            const content = folderDiv.querySelector(':scope > .folder-content');
            if (!content) return;

            if (content.style.display === 'none' || content.style.display === '') {
                content.style.display = 'block';
                icon.textContent = '[-]';
            } else {
                content.style.display = 'none';
                icon.textContent = '[+]';
            }
        }

        document.addEventListener('DOMContentLoaded', function() {
            document.querySelectorAll('.folder-name').forEach(function(name) {
                name.style.cursor = 'pointer';
                name.addEventListener('click', function() {
                    const icon = this.previousElementSibling;
                    if (icon && icon.classList.contains('folder-icon')) {
                        toggleFolder(icon);
                    }
                });
            });
        });";
    }

    private class DirectoryStats
    {
        public int TotalFiles { get; set; }
        public int TotalFolders { get; set; }
        public long TotalSize { get; set; }
    }
}
