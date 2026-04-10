#nullable enable

using PhotoSorterApp.Services;
using PhotoSorterApp.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace PhotoSorterApp.Tests;

/// <summary>
/// Диагностические тесты для проверки логики авто-распознавания лиц
/// </summary>
[TestFixture]
public class FaceAutoRecognitionDiagnosticsTests
{
    [Test]
    public void Test_CentroidCalculation_WithMultipleFaces()
    {
        var embeddings = new List<float[]>
        {
            new[] { 0.1f, 0.2f, 0.3f, 0.4f },
            new[] { 0.11f, 0.21f, 0.29f, 0.41f },
            new[] { 0.09f, 0.19f, 0.31f, 0.39f }
        };

        var centroid = CalculateCentroid(embeddings);

        Assert.That(centroid, Is.Not.Null);
        Assert.That(centroid.Length, Is.EqualTo(4));
        Assert.That(centroid[0], Is.InRange(0.09f, 0.11f));
    }

    [Test]
    public void Test_CosineSimilarity_SameVector()
    {
        var v1 = new[] { 0.6f, 0.8f };
        var v2 = new[] { 0.6f, 0.8f };

        var distance = CosineDistance(v1, v2);

        Assert.That(distance, Is.InRange(0, 0.01f));
    }

    [Test]
    public void Test_CosineSimilarity_DifferentVectors()
    {
        var v1 = new[] { 1.0f, 0.0f };
        var v2 = new[] { 0.0f, 1.0f };

        var distance = CosineDistance(v1, v2);

        Assert.That(distance, Is.InRange(0.99f, 1.01f));
    }

    [Test]
    public void Test_ThresholdLogic_ShouldMatchAtLowDistance()
    {
        double threshold = 0.55;
        double distance = 0.324;

        bool shouldMatch = distance <= threshold;

        Assert.That(shouldMatch, Is.True, $"Distance {distance} should be <= {threshold}");
    }

    [Test]
    public void Test_ThresholdLogic_ShouldNotMatchAtHighDistance()
    {
        double threshold = 0.45;
        double distance = 0.596;

        bool shouldMatch = distance <= threshold;

        Assert.That(shouldMatch, Is.False, $"Distance {distance} should be > {threshold}");
    }

    [Test]
    public void Test_FindBestPerson_WithMultipleCentroids()
    {
        var centroids = new Dictionary<int, float[]>
        {
            { 1, new[] { 0.1f, 0.2f, 0.3f, 0.4f } },
            { 2, new[] { 0.9f, 0.8f, 0.7f, 0.6f } },
            { 3, new[] { 0.5f, 0.5f, 0.5f, 0.5f } }
        };

        var candidate = new[] { 0.11f, 0.21f, 0.29f, 0.41f };
        double threshold = 0.5;

        bool found = TryFindBestPerson(centroids, candidate, threshold, out int personId, out double distance);

        Assert.That(found, Is.True, "Expected a matching person");
        Assert.That(personId, Is.EqualTo(1));
        Assert.That(distance, Is.InRange(0, 0.1f));
    }

    private static float[] CalculateCentroid(List<float[]> embeddings)
    {
        if (embeddings.Count == 0) return Array.Empty<float>();

        int dim = embeddings[0].Length;
        var sum = new double[dim];

        foreach (var emb in embeddings)
        {
            for (int i = 0; i < dim; i++)
                sum[i] += emb[i];
        }

        var centroid = new float[dim];
        for (int i = 0; i < dim; i++)
            centroid[i] = (float)(sum[i] / embeddings.Count);

        NormalizeInPlace(centroid);
        return centroid;
    }

    private static double CosineDistance(float[] v1, float[] v2)
    {
        var v1Norm = v1.ToArray();
        var v2Norm = v2.ToArray();
        NormalizeInPlace(v1Norm);
        NormalizeInPlace(v2Norm);

        double dot = 0;
        for (int i = 0; i < v1Norm.Length; i++)
            dot += v1Norm[i] * v2Norm[i];

        return 1.0 - dot;
    }

    private static void NormalizeInPlace(float[] v)
    {
        double sum = 0;
        for (int i = 0; i < v.Length; i++)
            sum += v[i] * v[i];

        double len = Math.Sqrt(sum);
        if (len < 1e-8) return;

        for (int i = 0; i < v.Length; i++)
            v[i] /= (float)len;
    }

    private static bool TryFindBestPerson(Dictionary<int, float[]> centroids, float[] candidate,
        double threshold, out int personId, out double distance)
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
}
