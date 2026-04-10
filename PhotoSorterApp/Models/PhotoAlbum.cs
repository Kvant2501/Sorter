#nullable enable

using System;
using System.Collections.Generic;

namespace PhotoSorterApp.Models;

public class PhotoAlbum
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSmartAlbum { get; set; }
    public string? RuleJson { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public ICollection<AlbumPhoto> Photos { get; set; } = new List<AlbumPhoto>();
}
