using System.Windows;

namespace PhotoSorterApp.Views;

public partial class InputDialog : Window
{
    public string Input => InputBox.Text;

    public InputDialog(string prompt, string defaultValue = "")
    {
        InitializeComponent();
        PromptText.Text = prompt;
        InputBox.Text = defaultValue;
        InputBox.SelectAll();
        InputBox.Focus();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}