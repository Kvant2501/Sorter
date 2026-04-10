#nullable enable

using PhotoSorterApp.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PhotoSorterApp.Services;

public class FaceClusteringService
{
    public IReadOnlyDictionary<int, int> BuildClusters(IReadOnlyList<DetectedFace> faces, double maxCosineDistance = 0.50)
    {
        var vectors = faces
            .Where(f => f.FaceEmbedding?.Vector is { Length: > 0 })
            .Select(f => (faceId: f.Id, vector: DecodeEmbedding(f.FaceEmbedding!.Vector)))
            .Where(x => x.vector.Length > 0)
            .ToDictionary(x => x.faceId, x => x.vector);

        var result = new Dictionary<int, int>(faces.Count);
        var parent = vectors.Keys.ToDictionary(id => id, id => id);

        int Find(int x)
        {
            var p = parent[x];
            while (p != parent[p]) p = parent[p];
            parent[x] = p;
            return p;
        }

        void Union(int a, int b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb)
                parent[rb] = ra;
        }

        var ids = vectors.Keys.ToList();
        for (int i = 0; i < ids.Count; i++)
        {
            for (int j = i + 1; j < ids.Count; j++)
            {
                var d = CosineDistance(vectors[ids[i]], vectors[ids[j]]);
                if (d <= maxCosineDistance)
                    Union(ids[i], ids[j]);
            }
        }

        var rootToCluster = new Dictionary<int, int>();
        var clusterCounter = 1;

        foreach (var face in faces)
        {
            if (!vectors.ContainsKey(face.Id))
            {
                result[face.Id] = 100000 + face.Id;
                continue;
            }

            var root = Find(face.Id);
            if (!rootToCluster.TryGetValue(root, out var clusterId))
            {
                clusterId = clusterCounter++;
                rootToCluster[root] = clusterId;
            }

            result[face.Id] = clusterId;
        }

        return result;
    }

    private static float[] DecodeEmbedding(byte[] bytes)
    {
        if (bytes.Length < sizeof(float) || bytes.Length % sizeof(float) != 0)
            return [];

        var values = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
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
