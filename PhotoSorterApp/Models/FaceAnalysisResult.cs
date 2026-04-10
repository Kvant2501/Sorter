#nullable enable

using System.Collections.Generic;

namespace PhotoSorterApp.Models;

public class FaceAnalysisResult
{
    public string ModelName { get; set; } = string.Empty;
    public List<FaceDetectionResult> Faces { get; set; } = new();
}

public class FaceDetectionResult
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Confidence { get; set; }
    public float[] Embedding { get; set; } = [];
}
