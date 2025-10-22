using System;
using System.Windows;

namespace PhotoSorterApp.Views;

public partial class ProgressDialog : Window
{
    private readonly Action _onCancel;

    public ProgressDialog(string title, string status, Action onCancel)
    {
        _onCancel = onCancel;
        InitializeComponent(); // ← ДОЛЖЕН БЫТЬ ПЕРВЫМ!

        // Теперь можно обращаться к элементам
        TitleTextBlock.Text = title;
        StatusTextBlock.Text = status;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _onCancel?.Invoke();
        Close();
    }
}