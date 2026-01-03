using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace PhotoSorterApp.Views;

public partial class PreviewWindow : Window
{
    public PreviewWindow(string filePath)
    {
        InitializeComponent();
        Title = Path.GetFileName(filePath);

        DocumentName.Text = Path.GetFileName(filePath);

        var loaded = TryLoadImage(filePath);
        if (!loaded)
        {
            try
            {
                var icon = System.Drawing.Icon.ExtractAssociatedIcon(filePath);
                if (icon != null)
                {
                    DocumentIcon.Source = Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromWidthAndHeight(128, 128));
                }
            }
            catch
            {
                // ignore icon errors
            }

            DocumentPreview.Visibility = Visibility.Visible;
        }
        else
        {
            DocumentPreview.Visibility = Visibility.Collapsed;
        }
    }

    private bool TryLoadImage(string filePath)
    {
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
            return true;
        }
        catch
        {
            PreviewImage.Source = null;
            return false;
        }
    }
}