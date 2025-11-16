#nullable enable

using System;
using System.Collections.ObjectModel;
using System.Windows.Media;

namespace PhotoSorterApp.Services;

public enum LogLevel
{
    Info,
    Warning,
    Error
}

public class LogEntry
{
    public string Message { get; set; }
    public Brush Color { get; set; }
    public string Icon { get; set; }
    public string Timestamp { get; set; } // ← ДОБАВЛЕНО!

    public LogEntry(string message, LogLevel level = LogLevel.Info, string icon = "✅")
    {
        Message = message;
        Icon = icon;
        Timestamp = DateTime.Now.ToString("HH:mm:ss"); // ← ДОБАВЛЕНО!
        Color = level switch
        {
            LogLevel.Warning => Brushes.Orange,
            LogLevel.Error => Brushes.Red,
            _ => Brushes.Black
        };
    }
}

public class LogCollection : ObservableCollection<LogEntry>
{
    public void Log(string message, LogLevel level = LogLevel.Info, string icon = "✅")
    {
        Add(new LogEntry(message, level, icon));
    }
}