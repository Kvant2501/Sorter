#nullable enable

using System;
using System.Collections.Generic;

namespace PhotoSorterApp.Models;

public class PhotoAsset
{
    public int Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string? FileHash { get; set; }
    public DateTime? CapturedAtUtc { get; set; }
    public DateTime IndexedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<DetectedFace> Faces { get; set; } = new List<DetectedFace>();
    public ICollection<PhotoTag> Tags { get; set; } = new List<PhotoTag>();
    public ICollection<AlbumPhoto> Albums { get; set; } = new List<AlbumPhoto>();
}
