#nullable enable

using PhotoSorterApp.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoSorterApp.Services;

public class FaceIndexingOptions
{
    public string SourceFolder { get; set; } = string.Empty;
    public bool IsRecursive { get; set; } = true;
    public double MinConfidence { get; set; } = 0.45;
    public int BatchSize { get; set; } = 1;
    public bool AutoAssignKnownPersons { get; set; } = true;
    public bool SkipAlreadyIndexedPhotos { get; set; } = false; // ? ????????: ?? ????????? FALSE
    public double AutoAssignThreshold { get; set; } = 0.55; // ????? ?????? ????? ??? ??????????????
}

public class FaceIndexingResult
{
    public int ProcessedFiles { get; set; }
    public int IndexedPhotos { get; set; }
    public int SavedFaces { get; set; }
    public int AutoAssignedFaces { get; set; }
    public int UnknownFaces { get; set; }
    public int FilesWithAcceptedFaces { get; set; }
    public int FilesWithoutAcceptedFaces { get; set; }
    public int SkippedAlreadyIndexedPhotos { get; set; }
    public int ReindexedBecauseMissingFaces { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class FaceIndexingPipelineService
{
    private readonly IFaceRecognitionClient _recognitionClient;
    private readonly IFaceCatalogService _catalogService;
    private readonly Action<string>? _logger;

    public FaceIndexingPipelineService(
        IFaceRecognitionClient recognitionClient,
        IFaceCatalogService catalogService,
        Action<string>? logger = null)
    {
        _recognitionClient = recognitionClient;
        _catalogService = catalogService;
        _logger = logger;
    }

    public async Task<FaceIndexingResult> RunAsync(FaceIndexingOptions options, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.SourceFolder) || !Directory.Exists(options.SourceFolder))
            throw new DirectoryNotFoundException("Папка для индексации не найдена.");

        var result = new FaceIndexingResult();
        var searchOption = options.IsRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var photoExtensions = SupportedFormats.GetExtensionsByProfile(FileTypeProfile.PhotosOnly);

        var files = Directory.EnumerateFiles(options.SourceFolder, "*.*", searchOption)
            .Where(x => photoExtensions.Contains(Path.GetExtension(x), StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (files.Count == 0)
        {
            progress?.Report(100);
            return result;
        }

        Dictionary<int, float[]> centroids = new();
        Dictionary<int, string> nameById = new();
        if (options.AutoAssignKnownPersons)
        {
            var knownPersons = await _catalogService.GetPersonsAsync(cancellationToken);
            nameById = knownPersons.ToDictionary(x => x.Id, x => x.DisplayName);
            var knownFaces = await _catalogService.GetConfirmedFacesWithEmbeddingsAsync(cancellationToken);
            centroids = BuildPersonCentroids(knownFaces);
        }

        var batchSize = Math.Max(1, options.BatchSize);

        for (var offset = 0; offset < files.Count; offset += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = files.Skip(offset).Take(batchSize).ToList();

            IReadOnlyDictionary<string, FaceAnalysisResult> analysisByPath;
            try
            {
                analysisByPath = await _recognitionClient.AnalyzeBatchAsync(batch, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"Face API batch failed, fallback to single mode: {ex.Message}");
                var fallback = new Dictionary<string, FaceAnalysisResult>(StringComparer.OrdinalIgnoreCase);

                foreach (var filePath in batch)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        using var singleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        singleCts.CancelAfter(TimeSpan.FromSeconds(25));
                        fallback[filePath] = await _recognitionClient.AnalyzeAsync(filePath, singleCts.Token);
                    }
                    catch (Exception singleEx)
                    {
                        result.Errors.Add($"{filePath}: Face API single failed: {singleEx.Message}");
                    }
                }

                analysisByPath = fallback;
            }

            foreach (var filePath in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.ProcessedFiles++;

                try
                {
                    var captured = MetadataService.GetPhotoDateTaken(filePath);
                    var capturedUtc = captured.HasValue
                        ? (DateTime?)DateTime.SpecifyKind(captured.Value, DateTimeKind.Local).ToUniversalTime()
                        : null;

                    var hash = ComputeHash(filePath);

                    if (options.SkipAlreadyIndexedPhotos)
                    {
                        var existingByHash = await _catalogService.FindPhotoByHashAsync(hash, cancellationToken);
                        _logger?.Invoke($"Hash: {Path.GetFileName(filePath)} -> {hash[..Math.Min(12, hash.Length)]}..., found={(existingByHash is null ? "no" : $"yes(id={existingByHash.Id}, path={Path.GetFileName(existingByHash.FilePath)})")}");

                        if (existingByHash is not null)
                        {
                            var alreadyHasFaces = await _catalogService.PhotoHasFacesAsync(existingByHash.Id, cancellationToken);
                            _logger?.Invoke($"Existing faces for id={existingByHash.Id}: {alreadyHasFaces}");
                            if (alreadyHasFaces)
                            {
                                result.IndexedPhotos++;
                                result.SkippedAlreadyIndexedPhotos++;
                                _logger?.Invoke($"Skip unchanged: {Path.GetFileName(filePath)}");
                                continue;
                            }

                            result.ReindexedBecauseMissingFaces++;
                            _logger?.Invoke($"Reindex required: hash exists but faces missing for id={existingByHash.Id}");
                        }
                    }

                    var photo = await _catalogService.UpsertPhotoAsync(filePath, hash, capturedUtc, cancellationToken);
                    // Remove stale unknown detections for this photo before saving fresh detections.
                    // Keep confirmed faces as training data for future auto-assignment.
                    await _catalogService.ClearUnconfirmedFacesForPhotoAsync(photo.Id, cancellationToken);
                    var existingFaces = await _catalogService.GetFacesForPhotoAsync(photo.Id, cancellationToken);
                    result.IndexedPhotos++;

                    analysisByPath.TryGetValue(filePath, out var analysis);
                    analysis ??= new FaceAnalysisResult { ModelName = "unknown" };

                    var acceptedFaces = analysis.Faces.Where(x => x.Confidence >= options.MinConfidence).ToList();
                    _logger?.Invoke($"Face API: {Path.GetFileName(filePath)} -> raw {analysis.Faces.Count}, accepted {acceptedFaces.Count}, threshold {options.MinConfidence:0.00}");

                    if (acceptedFaces.Count > 0)
                        result.FilesWithAcceptedFaces++;
                    else
                        result.FilesWithoutAcceptedFaces++;

                    foreach (var face in acceptedFaces)
                    {
                        var embeddingBytes = ConvertFloatArrayToBytes(face.Embedding);
                        if (embeddingBytes.Length == 0)
                            continue;

                        // Calculate Intersection Over Union (IoU) to prevent duplicating confirmed faces
                        double bestIoU = 0.0;
                        foreach (var existing in existingFaces)
                        {
                            var intersectionX = Math.Max(face.X, existing.BoundingBoxX);
                            var intersectionY = Math.Max(face.Y, existing.BoundingBoxY);
                            var intersectionW = Math.Min(face.X + face.Width, existing.BoundingBoxX + existing.BoundingBoxWidth) - intersectionX;
                            var intersectionH = Math.Min(face.Y + face.Height, existing.BoundingBoxY + existing.BoundingBoxHeight) - intersectionY;

                            if (intersectionW > 0 && intersectionH > 0)
                            {
                                var intersectionArea = intersectionW * intersectionH;
                                var unionArea = face.Width * face.Height + existing.BoundingBoxWidth * existing.BoundingBoxHeight - intersectionArea;
                                var iou = intersectionArea / unionArea;
                                if (iou > bestIoU)
                                    bestIoU = iou;
                            }
                        }

                        if (bestIoU > 0.5)
                        {
                            _logger?.Invoke($"  ? Skip face (already confirmed). IoU: {bestIoU:0.00}");
                            continue;
                        }

                        var savedFace = await _catalogService.AddDetectedFaceAsync(
                            photo.Id,
                            face.X,
                            face.Y,
                            face.Width,
                            face.Height,
                            face.Confidence,
                            embeddingBytes,
                            analysis.ModelName,
                            cancellationToken);

                        // Auto-assign known person by nearest centroid
                        var emb = face.Embedding;
                        if (options.AutoAssignKnownPersons && TryFindBestPerson(centroids, emb, options.AutoAssignThreshold, out var bestPersonId, out var distance))
                        {
                            await _catalogService.AssignFaceToPersonAsync(savedFace.Id, bestPersonId, cancellationToken);
                            if (nameById.TryGetValue(bestPersonId, out var personName))
                                await _catalogService.AddTagToPhotoAsync(photo.Id, personName, TagKind.Person, 1.0 - distance, "auto-recognition", cancellationToken);

                            result.AutoAssignedFaces++;
                            _logger?.Invoke($"  ? Face #{savedFace.Id} -> Person {bestPersonId} (dist {distance:0.000})");
                        }
                        else if (options.AutoAssignKnownPersons)
                        {
                            result.UnknownFaces++;
                            _logger?.Invoke($"  ? Face #{savedFace.Id} -> Unknown (no match, threshold {options.AutoAssignThreshold:0.00})");
                        }

                        result.SavedFaces++;
                    }

                    _logger?.Invoke($"Face indexing done: {filePath}");
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{filePath}: {ex.Message}");
                    _logger?.Invoke($"Face indexing failed: {filePath} - {ex.Message}");
                }

                var percent = files.Count == 0 ? 100 : Math.Min(100, result.ProcessedFiles * 100 / files.Count);
                progress?.Report(percent);
            }
        }

        return result;
    }

    private static string ComputeHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        return Convert.ToBase64String(sha256.ComputeHash(stream));
    }

    private static byte[] ConvertFloatArrayToBytes(float[] values)
    {
        if (values.Length == 0)
            return [];

        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static Dictionary<int, float[]> BuildPersonCentroids(IReadOnlyList<DetectedFace> knownFaces)
    {
        var byPerson = knownFaces
            .Where(f => f.ConfirmedPersonId != null && f.FaceEmbedding?.Vector is { Length: > 0 })
            .GroupBy(f => f.ConfirmedPersonId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(f => DecodeEmbedding(f.FaceEmbedding!.Vector)).Where(v => v.Length > 0).ToList());

        var result = new Dictionary<int, float[]>();
        foreach (var kv in byPerson)
        {
            if (kv.Value.Count == 0)
                continue;

            var dim = kv.Value.Max(v => v.Length);
            var sum = new double[dim];
            var count = 0;
            foreach (var vec in kv.Value)
            {
                if (vec.Length == 0)
                    continue;
                var len = Math.Min(dim, vec.Length);
                for (var i = 0; i < len; i++)
                    sum[i] += vec[i];
                count++;
            }

            if (count == 0)
                continue;

            var centroid = new float[dim];
            for (var i = 0; i < dim; i++)
                centroid[i] = (float)(sum[i] / count);

            NormalizeInPlace(centroid);
            result[kv.Key] = centroid;
        }

        return result;
    }

    private static bool TryFindBestPerson(Dictionary<int, float[]> centroids, float[] candidate, double threshold, out int personId, out double distance)
    {
        personId = default;
        distance = 1.0;

        if (centroids.Count == 0 || candidate.Length == 0)
            return false;

        var normalized = candidate.ToArray();
        NormalizeInPlace(normalized);

        foreach (var kv in centroids)
        {
            var d = CosineDistance(kv.Value, normalized);
            if (d < distance)
            {
                distance = d;
                personId = kv.Key;
            }
        }

        return distance <= threshold;
    }

    private static float[] DecodeEmbedding(byte[] bytes)
    {
        if (bytes.Length < sizeof(float) || bytes.Length % sizeof(float) != 0)
            return [];

        var values = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }

    private static void NormalizeInPlace(float[] v)
    {
        double sum = 0;
        for (var i = 0; i < v.Length; i++)
            sum += v[i] * v[i];

        var norm = Math.Sqrt(sum);
        if (norm <= double.Epsilon)
            return;

        for (var i = 0; i < v.Length; i++)
            v[i] = (float)(v[i] / norm);
    }

    private static double CosineDistance(float[] a, float[] b)
    {
        var len = Math.Min(a.Length, b.Length);
        if (len == 0)
            return 1.0;

        double dot = 0;
        double na = 0;
        double nb = 0;

        for (var i = 0; i < len; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }

        if (na <= double.Epsilon || nb <= double.Epsilon)
            return 1.0;

        var cosine = dot / (Math.Sqrt(na) * Math.Sqrt(nb));
        return 1.0 - cosine;
    }
}
