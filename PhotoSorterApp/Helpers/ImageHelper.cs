#nullable enable

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System.IO;
using System.Windows.Media.Imaging;

namespace PhotoSorterApp.Helpers;

public static class ImageHelper
{
    /// <summary>
    /// Загружает миниатюру для списка (маленькая, 80x60)
    /// </summary>
    public static BitmapImage? LoadOrientedThumbnail(string filePath, int width = 80, int height = 60)
    {
        try
        {
            using var image = Image.Load(filePath);
            image.Mutate(x => x.AutoOrient());

            using var memoryStream = new MemoryStream();
            var encoder = filePath.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)
                ? (SixLabors.ImageSharp.Formats.IImageEncoder)new SixLabors.ImageSharp.Formats.Png.PngEncoder()
                : new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder();

            image.Save(memoryStream, encoder);
            memoryStream.Seek(0, SeekOrigin.Begin);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = memoryStream;
            bitmap.DecodePixelWidth = width;
            bitmap.DecodePixelHeight = height;
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

    /// <summary>
    /// Загружает превью для ToolTip (большое, 300x200)
    /// </summary>
    public static BitmapImage? LoadPreviewThumbnail(string filePath, int width = 300, int height = 200)
    {
        try
        {
            using var image = Image.Load(filePath);
            image.Mutate(x => x.AutoOrient());

            using var memoryStream = new MemoryStream();
            var encoder = filePath.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)
                ? (SixLabors.ImageSharp.Formats.IImageEncoder)new SixLabors.ImageSharp.Formats.Png.PngEncoder()
                : new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder();

            image.Save(memoryStream, encoder);
            memoryStream.Seek(0, SeekOrigin.Begin);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = memoryStream;
            bitmap.DecodePixelWidth = width;
            bitmap.DecodePixelHeight = height;
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
}