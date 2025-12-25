using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace PhotoSorterApp.Views;

public partial class PreviewWindow : Window
{
    public PreviewWindow(string filePath)
    {
        InitializeComponent();
        Title = Path.GetFileName(filePath);
        try
        {
            var bitmap = new BitmapImage();
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                bitmap.BeginInit();
                bitmap.StreamSource = stream;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
            }
            PreviewImage.Source = bitmap;
        }
        catch
        {
            // ignore preview errors
        }
    }
}