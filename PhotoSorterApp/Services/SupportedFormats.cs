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
            FileTypeProfile.AllSupported => new[] { ".jpg", ".jpeg", ".png", ".bmp", ".tiff", ".cr2", ".cr3", ".nef", ".arw", ".dng", ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".m4v", ".pdf", ".docx", ".xlsx", ".txt" },
            _ => System.Array.Empty<string>()
        };
    }
}