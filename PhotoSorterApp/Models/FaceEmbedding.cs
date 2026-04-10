#nullable enable

using System;

namespace PhotoSorterApp.Models;

public class FaceEmbedding
{
    public int Id { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public int Dimension { get; set; }
    public byte[] Vector { get; set; } = Array.Empty<byte>();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DetectedFace? Face { get; set; }
}
