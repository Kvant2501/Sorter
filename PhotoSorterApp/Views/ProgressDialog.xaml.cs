using System;
using System.Windows;

namespace PhotoSorterApp.Views;

public partial class ProgressDialog : Window
{
    public ProgressDialog(string title, string statusMessage)
    {
        InitializeComponent();
        Title = title;
        StatusTextBlock.Text = statusMessage;
    }

    public bool IsCanceled { get; private set; }

    public event EventHandler? CancelRequested;

    public void UpdateStatus(string message)
    {
        StatusTextBlock.Text = message;
    }

    public void UpdateDetail(string message)
    {
        DetailTextBlock.Text = message;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        // Помечаем, что пользователь запросил отмену и уведомляем подписчиков.
        IsCanceled = true;
        try
        {
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // Игнорируем любые ошибки обработчиков
        }
        finally
        {
            Close();
        }
    }
}