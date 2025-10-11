using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace PhotoSorterApp.Helpers;

public class FileToPreviewConverter : IValueConverter
{
    private static readonly HashSet<string> _supportedImageFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp"
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string filePath || !File.Exists(filePath))
            return null;

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (!_supportedImageFormats.Contains(ext))
            return null;

        try
        {
            using var image = Image.Load(filePath);
            image.Mutate(x => x.AutoOrient().Resize(80, 60));

            var memoryStream = new MemoryStream();
            image.SaveAsJpeg(memoryStream);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = memoryStream;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}