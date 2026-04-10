#nullable enable

using System;

namespace PhotoSorterApp.Models;

public class AlbumPhoto
{
    public int PhotoAlbumId { get; set; }
    public PhotoAlbum PhotoAlbum { get; set; } = null!;

    public int PhotoAssetId { get; set; }
    public PhotoAsset PhotoAsset { get; set; } = null!;

    public int SortOrder { get; set; }
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
}
