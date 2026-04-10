#nullable enable

using System;

namespace PhotoSorterApp.Models;

public class DetectedFace
{
    public int Id { get; set; }

    public int PhotoAssetId { get; set; }
    public PhotoAsset PhotoAsset { get; set; } = null!;

    public double BoundingBoxX { get; set; }
    public double BoundingBoxY { get; set; }
    public double BoundingBoxWidth { get; set; }
    public double BoundingBoxHeight { get; set; }
    public double Confidence { get; set; }

    public int? FaceEmbeddingId { get; set; }
    public FaceEmbedding? FaceEmbedding { get; set; }

    public int? ConfirmedPersonId { get; set; }
    public FacePerson? ConfirmedPerson { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
