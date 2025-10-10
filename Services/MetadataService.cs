using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using System;
using System.Linq;

namespace PhotoSorterApp.Services;

public static class MetadataService
{
    public static DateTime? GetPhotoDateTaken(string filePath)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(filePath);
            var subIfdDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            var exifDirectory = directories.OfType<ExifIfd0Directory>().FirstOrDefault();

            if (subIfdDirectory?.TryGetDateTime(ExifSubIfdDirectory.TagDateTimeOriginal, out var dateTimeOriginal) == true)
                return dateTimeOriginal;

            if (subIfdDirectory?.TryGetDateTime(ExifSubIfdDirectory.TagDateTimeDigitized, out var dateTimeDigitized) == true)
                return dateTimeDigitized;

            if (exifDirectory?.TryGetDateTime(ExifIfd0Directory.TagDateTime, out var dateTime) == true)
                return dateTime;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка чтения EXIF для {filePath}: {ex.Message}");
        }

        return null;
    }
}