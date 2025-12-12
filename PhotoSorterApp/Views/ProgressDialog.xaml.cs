using System.Windows;

namespace PhotoSorterApp.Views;

public partial class ProgressDialog : Window
{
    // ИСПРАВЛЕНО: Конструктор принимает ТОЛЬКО 2 аргумента
    public ProgressDialog(string title, string statusMessage)
    {
        InitializeComponent();
        Title = title;
        StatusTextBlock.Text = statusMessage;
    }

    public void UpdateStatus(string message)
    {
        StatusTextBlock.Text = message;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}