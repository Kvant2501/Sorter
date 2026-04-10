#nullable enable

using PhotoSorterApp.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoSorterApp.Services;

public class LocalFaceRecognitionClient : IFaceRecognitionClient
{
    private readonly HttpClient _httpClient;

    public LocalFaceRecognitionClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<FaceAnalysisResult> AnalyzeAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        var prepared = PrepareAnalyzeRequest(imagePath);

        var response = await _httpClient.PostAsJsonAsync("/analyze", prepared.Request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<AnalyzeResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Face API returned empty response.");

        return Map(payload, prepared.ScaleX, prepared.ScaleY);
    }

    public async Task<IReadOnlyDictionary<string, FaceAnalysisResult>> AnalyzeBatchAsync(
        IReadOnlyList<string> imagePaths,
        CancellationToken cancellationToken = default)
    {
        var preparedItems = PrepareBatchRequests(imagePaths);
        var response = await _httpClient.PostAsJsonAsync("/analyze-batch", new AnalyzeBatchRequest(preparedItems.Select(x => x.Request).ToList()), cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return await FallbackBatchAsync(imagePaths, cancellationToken);
        }

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<AnalyzeBatchResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Face API returned empty batch response.");

        var scaleByPath = preparedItems.ToDictionary(x => x.Request.ImagePath, x => (x.ScaleX, x.ScaleY), StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, FaceAnalysisResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in payload.Items)
        {
            if (string.IsNullOrWhiteSpace(item.ImagePath))
                continue;

            var scale = scaleByPath.TryGetValue(item.ImagePath, out var s) ? s : (1d, 1d);
            result[item.ImagePath] = Map(item.Analysis ?? new AnalyzeResponse(), scale.Item1, scale.Item2);
        }

        return result;
    }

    private static PreparedAnalyzeRequest PrepareAnalyzeRequest(string imagePath)
    {
        var payload = TryReadFileAsBase64(imagePath);
        return new PreparedAnalyzeRequest(new AnalyzeRequest(imagePath, payload?.Base64), payload?.ScaleX ?? 1d, payload?.ScaleY ?? 1d);
    }

    private static List<PreparedAnalyzeRequest> PrepareBatchRequests(IReadOnlyList<string> imagePaths)
    {
        var items = new List<PreparedAnalyzeRequest>(imagePaths.Count);
        foreach (var path in imagePaths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            items.Add(PrepareAnalyzeRequest(path));
        }

        return items;
    }

    private static EncodedImagePayload? TryReadFileAsBase64(string imagePath)
    {
        try
        {
            if (!File.Exists(imagePath))
                return null;

            var ext = Path.GetExtension(imagePath);
            var isImage = !string.IsNullOrWhiteSpace(ext) &&
                          (ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                           ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                           ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                           ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
                           ext.Equals(".webp", StringComparison.OrdinalIgnoreCase));

            if (!isImage)
            {
                var raw = File.ReadAllBytes(imagePath);
                return new EncodedImagePayload(Convert.ToBase64String(raw), 1d, 1d);
            }

            using var image = Image.Load(imagePath);
            var originalWidth = image.Width;
            var originalHeight = image.Height;

            var maxSide = Math.Max(image.Width, image.Height);
            if (maxSide > 1600)
            {
                var ratio = 1600.0 / maxSide;
                var targetWidth = Math.Max(1, (int)Math.Round(image.Width * ratio));
                var targetHeight = Math.Max(1, (int)Math.Round(image.Height * ratio));
                image.Mutate(x => x.Resize(targetWidth, targetHeight));
            }

            using var ms = new MemoryStream();
            image.Save(ms, new JpegEncoder { Quality = 82 });

            var scaleX = image.Width == 0 ? 1d : (double)originalWidth / image.Width;
            var scaleY = image.Height == 0 ? 1d : (double)originalHeight / image.Height;

            return new EncodedImagePayload(Convert.ToBase64String(ms.ToArray()), scaleX, scaleY);
        }
        catch
        {
            return null;
        }
    }

    private async Task<IReadOnlyDictionary<string, FaceAnalysisResult>> FallbackBatchAsync(IReadOnlyList<string> imagePaths, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, FaceAnalysisResult>(imagePaths.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var path in imagePaths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            result[path] = await AnalyzeAsync(path, cancellationToken);
        }

        return result;
    }

    private static FaceAnalysisResult Map(AnalyzeResponse payload, double scaleX, double scaleY)
    {
        var result = new FaceAnalysisResult
        {
            ModelName = payload.Model ?? "unknown"
        };

        foreach (var item in payload.Faces)
        {
            result.Faces.Add(new FaceDetectionResult
            {
                X = item.X * scaleX,
                Y = item.Y * scaleY,
                Width = item.Width * scaleX,
                Height = item.Height * scaleY,
                Confidence = item.Confidence,
                Embedding = item.Embedding ?? []
            });
        }

        return result;
    }

    public static LocalFaceRecognitionClient CreateDefault()
    {
        var baseUrl = Environment.GetEnvironmentVariable("PHOTOSORTER_FACE_API_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = "http://localhost:5272";

        var timeoutSeconds = 180;
        var timeoutEnv = Environment.GetEnvironmentVariable("PHOTOSORTER_FACE_API_TIMEOUT_SECONDS");
        if (int.TryParse(timeoutEnv, out var parsed) && parsed > 0)
            timeoutSeconds = parsed;

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };

        return new LocalFaceRecognitionClient(httpClient);
    }

    private sealed record AnalyzeRequest(
        [property: JsonPropertyName("imagePath")] string ImagePath,
        [property: JsonPropertyName("imageBase64")] string? ImageBase64);

    private sealed record AnalyzeBatchRequest([property: JsonPropertyName("items")] IReadOnlyList<AnalyzeRequest> Items);

    private sealed record PreparedAnalyzeRequest(AnalyzeRequest Request, double ScaleX, double ScaleY);
    private sealed record EncodedImagePayload(string Base64, double ScaleX, double ScaleY);

    private sealed class AnalyzeResponse
    {
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("faces")]
        public List<FaceItem> Faces { get; set; } = new();
    }

    private sealed class AnalyzeBatchResponse
    {
        [JsonPropertyName("items")]
        public List<AnalyzeBatchItem> Items { get; set; } = new();
    }

    private sealed class AnalyzeBatchItem
    {
        [JsonPropertyName("imagePath")]
        public string? ImagePath { get; set; }

        [JsonPropertyName("analysis")]
        public AnalyzeResponse? Analysis { get; set; }
    }

    private sealed class FaceItem
    {
        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }

        [JsonPropertyName("width")]
        public double Width { get; set; }

        [JsonPropertyName("height")]
        public double Height { get; set; }

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }

        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }
}
