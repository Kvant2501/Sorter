// Services/LogCollection.cs
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
    public System.Windows.Media.Brush Color { get; set; }

    public LogEntry(string message, LogLevel level = LogLevel.Info)
    {
        Message = message;
        Color = level switch
        {
            LogLevel.Warning => System.Windows.Media.Brushes.Orange,
            LogLevel.Error => System.Windows.Media.Brushes.Red,
            _ => System.Windows.Media.Brushes.Black
        };
    }
}

public class LogCollection : System.Collections.ObjectModel.ObservableCollection<LogEntry>
{
    public void Log(string message, LogLevel level = LogLevel.Info)
    {
        Add(new LogEntry(message, level));
    }
}