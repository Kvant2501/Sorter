using PhotoSorterApp.Models;

namespace PhotoSorterApp.Services;

public static class SupportedFormats
{
    public static string[] GetExtensionsByProfile(FileTypeProfile profile)
    {
        return profile switch
        {
            FileTypeProfile.PhotosOnly => new[] { ".jpg", ".jpeg", ".png", ".bmp", ".tiff", ".cr2", ".cr3", ".nef", ".arw", ".dng" },
            FileTypeProfile.VideosOnly => new[] { ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".m4v" },
            FileTypeProfile.PhotosAndVideos => new[] { ".jpg", ".jpeg", ".png", ".bmp", ".tiff", ".cr2", ".cr3", ".nef", ".arw", ".dng", ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".m4v" },
            FileTypeProfile.DocumentsOnly => new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf" },
            _ => System.Array.Empty<string>()
        };
    }
}