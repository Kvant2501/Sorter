using NUnit.Framework;
using PhotoSorterApp.Models;
using PhotoSorterApp.Services;
using System;
using System.Collections.Generic;

namespace PhotoSorterApp.Tests;

[TestFixture]
public class FaceClusteringServiceTests
{
    [Test]
    public void BuildClusters_GroupsNearbyEmbeddings()
    {
        var service = new FaceClusteringService();

        var faces = new List<DetectedFace>
        {
            CreateFace(1, new float[] { 1f, 0f, 0f }),
            CreateFace(2, new float[] { 0.99f, 0.01f, 0f }),
            CreateFace(3, new float[] { 0f, 1f, 0f })
        };

        var clusters = service.BuildClusters(faces, maxCosineDistance: 0.2);

        Assert.That(clusters[1], Is.EqualTo(clusters[2]));
        Assert.That(clusters[3], Is.Not.EqualTo(clusters[1]));
    }

    [Test]
    public void BuildClusters_AssignsStandaloneCluster_WhenEmbeddingMissing()
    {
        var service = new FaceClusteringService();
        var faces = new List<DetectedFace>
        {
            new() { Id = 1, FaceEmbedding = null },
            CreateFace(2, new float[] { 1f, 0f })
        };

        var clusters = service.BuildClusters(faces);

        Assert.That(clusters.ContainsKey(1), Is.True);
        Assert.That(clusters[1], Is.Not.EqualTo(clusters[2]));
    }

    private static DetectedFace CreateFace(int id, float[] embedding)
    {
        var bytes = new byte[embedding.Length * sizeof(float)];
        Buffer.BlockCopy(embedding, 0, bytes, 0, bytes.Length);

        return new DetectedFace
        {
            Id = id,
            FaceEmbedding = new FaceEmbedding { Vector = bytes, Dimension = embedding.Length, ModelName = "test" }
        };
    }
}
