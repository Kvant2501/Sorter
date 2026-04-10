#nullable enable

namespace PhotoSorterApp.Models;

public class PhotoTag
{
    public int PhotoAssetId { get; set; }
    public PhotoAsset PhotoAsset { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;

    public double? Confidence { get; set; }
    public string Source { get; set; } = "system";
}
