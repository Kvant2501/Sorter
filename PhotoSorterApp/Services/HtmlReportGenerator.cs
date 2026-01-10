using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PhotoSorterApp.Services;

public static class HtmlReportGenerator
{
    public static string GenerateTransferReport(
        string sourceFolder,
        string destinationFolder,
        List<TransferFileInfo> files,
        TransferStatistics stats)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang='ru'>");
        sb.AppendLine("<head>");
        sb.AppendLine("    <meta charset='UTF-8'>");
        sb.AppendLine("    <meta name='viewport' content='width=device-width, initial-scale=1.0'>");
        sb.AppendLine("    <title>Отчёт о переносе файлов</title>");
        sb.AppendLine("    <style>");
        sb.AppendLine(GetStyles());
        sb.AppendLine("    </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        
        // Header
        sb.AppendLine("    <div class='container'>");
        sb.AppendLine("        <header>");
        sb.AppendLine("            <h1>?? Отчёт о переносе файлов</h1>");
        sb.AppendLine($"            <p class='date'>Дата создания: {DateTime.Now:dd.MM.yyyy HH:mm:ss}</p>");
        sb.AppendLine("        </header>");
        
        // Summary section
        sb.AppendLine("        <section class='summary'>");
        sb.AppendLine("            <h2>?? Сводка</h2>");
        sb.AppendLine("            <div class='stats-grid'>");
        sb.AppendLine($"                <div class='stat-card'>");
        sb.AppendLine($"                    <div class='stat-value'>{stats.TotalFiles}</div>");
        sb.AppendLine($"                    <div class='stat-label'>Всего файлов</div>");
        sb.AppendLine($"                </div>");
        sb.AppendLine($"                <div class='stat-card success'>");
        sb.AppendLine($"                    <div class='stat-value'>{stats.ProcessedFiles}</div>");
        sb.AppendLine($"                    <div class='stat-label'>Обработано</div>");
        sb.AppendLine($"                </div>");
        sb.AppendLine($"                <div class='stat-card error'>");
        sb.AppendLine($"                    <div class='stat-value'>{stats.ErrorFiles}</div>");
        sb.AppendLine($"                    <div class='stat-label'>Ошибок</div>");
        sb.AppendLine($"                </div>");
        sb.AppendLine($"                <div class='stat-card'>");
        sb.AppendLine($"                    <div class='stat-value'>{FormatSize(stats.TotalSize)}</div>");
        sb.AppendLine($"                    <div class='stat-label'>Общий размер</div>");
        sb.AppendLine($"                </div>");
        sb.AppendLine("            </div>");
        sb.AppendLine("        </section>");
        
        // Paths section
        sb.AppendLine("        <section class='paths'>");
        sb.AppendLine("            <h2>?? Пути</h2>");
        sb.AppendLine("            <div class='path-info'>");
        sb.AppendLine($"                <div class='path-label'>Источник:</div>");
        sb.AppendLine($"                <div class='path-value'>{EscapeHtml(sourceFolder)}</div>");
        sb.AppendLine("            </div>");
        sb.AppendLine("            <div class='path-info'>");
        sb.AppendLine($"                <div class='path-label'>Назначение:</div>");
        sb.AppendLine($"                <div class='path-value'>{EscapeHtml(destinationFolder)}</div>");
        sb.AppendLine("            </div>");
        sb.AppendLine("        </section>");
        
        // Files table
        sb.AppendLine("        <section class='files'>");
        sb.AppendLine("            <h2>?? Файлы</h2>");
        
        // Filter buttons
        sb.AppendLine("            <div class='filter-buttons'>");
        sb.AppendLine("                <button class='filter-btn active' onclick='filterFiles(\"all\")'>Все</button>");
        sb.AppendLine("                <button class='filter-btn' onclick='filterFiles(\"success\")'>Успешные</button>");
        sb.AppendLine("                <button class='filter-btn' onclick='filterFiles(\"error\")'>Ошибки</button>");
        sb.AppendLine("            </div>");
        
        sb.AppendLine("            <table>");
        sb.AppendLine("                <thead>");
        sb.AppendLine("                    <tr>");
        sb.AppendLine("                        <th>№</th>");
        sb.AppendLine("                        <th>Имя файла</th>");
        sb.AppendLine("                        <th>Размер</th>");
        sb.AppendLine("                        <th>Новый путь</th>");
        sb.AppendLine("                        <th>Статус</th>");
        sb.AppendLine("                    </tr>");
        sb.AppendLine("                </thead>");
        sb.AppendLine("                <tbody>");
        
        int index = 1;
        foreach (var file in files.OrderBy(f => f.Status).ThenBy(f => f.OriginalPath))
        {
            var statusClass = file.Status == TransferStatus.Success ? "success" : "error";
            var statusIcon = file.Status == TransferStatus.Success ? "?" : "?";
            var statusText = file.Status == TransferStatus.Success ? "Успешно" : "Ошибка";
            
            sb.AppendLine($"                <tr class='file-row {statusClass}' data-status='{statusClass}'>");
            sb.AppendLine($"                    <td>{index++}</td>");
            sb.AppendLine($"                    <td class='filename'>");
            sb.AppendLine($"                        <span class='file-icon'>{GetFileIcon(file.OriginalPath)}</span>");
            sb.AppendLine($"                        {EscapeHtml(Path.GetFileName(file.OriginalPath))}");
            sb.AppendLine($"                    </td>");
            sb.AppendLine($"                    <td>{FormatSize(file.Size)}</td>");
            sb.AppendLine($"                    <td>");
            
            if (file.Status == TransferStatus.Success)
            {
                // Create clickable link to file
                var fileLink = $"file:///{file.NewPath.Replace("\\", "/")}";
                sb.AppendLine($"                        <a href='{fileLink}' class='file-link' title='Открыть файл'>");
                sb.AppendLine($"                            {EscapeHtml(file.NewPath)}");
                sb.AppendLine($"                        </a>");
            }
            else
            {
                sb.AppendLine($"                        <span class='error-text'>{EscapeHtml(file.ErrorMessage ?? "")}</span>");
            }
            
            sb.AppendLine($"                    </td>");
            sb.AppendLine($"                    <td><span class='status-badge {statusClass}'>{statusIcon} {statusText}</span></td>");
            sb.AppendLine($"                </tr>");
        }
        
        sb.AppendLine("                </tbody>");
        sb.AppendLine("            </table>");
        sb.AppendLine("        </section>");
        
        // Footer
        sb.AppendLine("        <footer>");
        sb.AppendLine("            <p>Создано с помощью PhotoSorter v1.0.2</p>");
        sb.AppendLine("        </footer>");
        sb.AppendLine("    </div>");
        
        // JavaScript for filtering
        sb.AppendLine("    <script>");
        sb.AppendLine(GetJavaScript());
        sb.AppendLine("    </script>");
        
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        
        return sb.ToString();
    }

    private static string GetStyles()
    {
        return @"
        * {
            margin: 0;
            padding: 0;
            box-sizing: border-box;
        }
        
        body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background: #f5f7fa;
            color: #333;
            line-height: 1.6;
            padding: 20px;
        }
        
        .container {
            max-width: 1400px;
            margin: 0 auto;
            background: white;
            border-radius: 12px;
            box-shadow: 0 2px 20px rgba(0,0,0,0.1);
            overflow: hidden;
        }
        
        header {
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            padding: 40px;
            text-align: center;
        }
        
        header h1 {
            font-size: 2.5em;
            margin-bottom: 10px;
        }
        
        .date {
            opacity: 0.9;
            font-size: 1.1em;
        }
        
        section {
            padding: 30px 40px;
            border-bottom: 1px solid #e0e0e0;
        }
        
        section:last-of-type {
            border-bottom: none;
        }
        
        h2 {
            color: #667eea;
            margin-bottom: 20px;
            font-size: 1.8em;
        }
        
        .stats-grid {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
            gap: 20px;
            margin-top: 20px;
        }
        
        .stat-card {
            background: #f8f9fa;
            padding: 25px;
            border-radius: 10px;
            text-align: center;
            border: 2px solid #e0e0e0;
            transition: transform 0.2s, box-shadow 0.2s;
        }
        
        .stat-card:hover {
            transform: translateY(-5px);
            box-shadow: 0 5px 15px rgba(0,0,0,0.1);
        }
        
        .stat-card.success {
            border-color: #10b981;
            background: #ecfdf5;
        }
        
        .stat-card.error {
            border-color: #ef4444;
            background: #fef2f2;
        }
        
        .stat-value {
            font-size: 2.5em;
            font-weight: bold;
            color: #667eea;
            margin-bottom: 10px;
        }
        
        .stat-card.success .stat-value {
            color: #10b981;
        }
        
        .stat-card.error .stat-value {
            color: #ef4444;
        }
        
        .stat-label {
            color: #666;
            font-size: 1em;
        }
        
        .path-info {
            display: grid;
            grid-template-columns: 150px 1fr;
            gap: 15px;
            margin-bottom: 15px;
            padding: 15px;
            background: #f8f9fa;
            border-radius: 8px;
        }
        
        .path-label {
            font-weight: bold;
            color: #667eea;
        }
        
        .path-value {
            word-break: break-all;
            color: #333;
        }
        
        .filter-buttons {
            margin-bottom: 20px;
            display: flex;
            gap: 10px;
        }
        
        .filter-btn {
            padding: 10px 20px;
            border: 2px solid #667eea;
            background: white;
            color: #667eea;
            border-radius: 6px;
            cursor: pointer;
            font-size: 1em;
            transition: all 0.3s;
        }
        
        .filter-btn:hover {
            background: #667eea;
            color: white;
        }
        
        .filter-btn.active {
            background: #667eea;
            color: white;
        }
        
        table {
            width: 100%;
            border-collapse: collapse;
            margin-top: 20px;
        }
        
        thead {
            background: #667eea;
            color: white;
        }
        
        th, td {
            padding: 15px;
            text-align: left;
            border-bottom: 1px solid #e0e0e0;
        }
        
        th {
            font-weight: 600;
            text-transform: uppercase;
            font-size: 0.9em;
            letter-spacing: 0.5px;
        }
        
        tbody tr {
            transition: background 0.2s;
        }
        
        tbody tr:hover {
            background: #f8f9fa;
        }
        
        .filename {
            font-weight: 500;
            display: flex;
            align-items: center;
            gap: 8px;
        }
        
        .file-icon {
            font-size: 1.3em;
        }
        
        .file-link {
            color: #667eea;
            text-decoration: none;
            word-break: break-all;
            transition: color 0.2s;
        }
        
        .file-link:hover {
            color: #764ba2;
            text-decoration: underline;
        }
        
        .status-badge {
            display: inline-block;
            padding: 6px 12px;
            border-radius: 20px;
            font-size: 0.9em;
            font-weight: 600;
        }
        
        .status-badge.success {
            background: #ecfdf5;
            color: #10b981;
        }
        
        .status-badge.error {
            background: #fef2f2;
            color: #ef4444;
        }
        
        .error-text {
            color: #ef4444;
            font-style: italic;
        }
        
        footer {
            text-align: center;
            padding: 20px;
            color: #666;
            background: #f8f9fa;
        }
        
        @media print {
            body {
                background: white;
            }
            .filter-buttons {
                display: none;
            }
            .container {
                box-shadow: none;
            }
        }";
    }

    private static string GetJavaScript()
    {
        return @"
        function filterFiles(status) {
            const rows = document.querySelectorAll('.file-row');
            const buttons = document.querySelectorAll('.filter-btn');
            
            buttons.forEach(btn => btn.classList.remove('active'));
            event.target.classList.add('active');
            
            rows.forEach(row => {
                if (status === 'all') {
                    row.style.display = '';
                } else {
                    row.style.display = row.dataset.status === status ? '' : 'none';
                }
            });
        }";
    }

    private static string GetFileIcon(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" => "???",
            ".mp4" or ".mov" or ".avi" or ".mkv" => "??",
            ".pdf" => "??",
            ".doc" or ".docx" => "??",
            ".xls" or ".xlsx" => "??",
            ".ppt" or ".pptx" => "??",
            ".txt" => "??",
            ".zip" or ".rar" or ".7z" => "??",
            _ => "??"
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
}

public class TransferFileInfo
{
    public string OriginalPath { get; set; } = string.Empty;
    public string NewPath { get; set; } = string.Empty;
    public long Size { get; set; }
    public TransferStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
}

public class TransferStatistics
{
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public int ErrorFiles { get; set; }
    public long TotalSize { get; set; }
}

public enum TransferStatus
{
    Success,
    Error
}
