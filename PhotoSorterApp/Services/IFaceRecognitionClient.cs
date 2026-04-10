#nullable enable

using PhotoSorterApp.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoSorterApp.Services;

public interface IFaceRecognitionClient
{
    Task<FaceAnalysisResult> AnalyzeAsync(string imagePath, CancellationToken cancellationToken = default);

    async Task<IReadOnlyDictionary<string, FaceAnalysisResult>> AnalyzeBatchAsync(
        IReadOnlyList<string> imagePaths,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, FaceAnalysisResult>(imagePaths.Count);
        foreach (var path in imagePaths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            result[path] = await AnalyzeAsync(path, cancellationToken);
        }

        return result;
    }
}
