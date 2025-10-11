using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;
using System.Windows.Media.Imaging;

namespace PhotoSorterApp.Models;

public partial class DuplicateItem : ObservableObject
{
    public string FilePath { get; }
    public long FileSize { get; }
    public string FileName => Path.GetFileName(FilePath);
    public string DisplaySize => $"{FileSize / 1024.0:F1} KB";
    public BitmapImage? Thumbnail { get; }

    [ObservableProperty]
    private bool _isSelected;

    public DuplicateItem(string filePath)
    {
        FilePath = filePath;
        FileSize = new FileInfo(filePath).Length;
        Thumbnail = Helpers.ImageHelper.LoadOrientedThumbnail(filePath);
    }
}