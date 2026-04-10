using NUnit.Framework;
using PhotoSorterApp.Models;
using PhotoSorterApp.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoSorterApp.Tests;

[TestFixture]
public class FaceIndexingPipelineServiceTests
{
    [Test]
    public async Task RunAsync_SavesFacesFromRecognitionResponse()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PhotoSorterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var photoPath = Path.Combine(tempDir, "sample.jpg");
            await File.WriteAllBytesAsync(photoPath, new byte[] { 1, 2, 3, 4, 5 });

            var fakeRecognition = new FakeRecognitionClient();
            var fakeCatalog = new FakeCatalogService();
            var pipeline = new FaceIndexingPipelineService(fakeRecognition, fakeCatalog);

            var result = await pipeline.RunAsync(new FaceIndexingOptions
            {
                SourceFolder = tempDir,
                IsRecursive = false,
                MinConfidence = 0.5
            });

            Assert.That(result.ProcessedFiles, Is.EqualTo(1));
            Assert.That(result.SavedFaces, Is.EqualTo(1));
            Assert.That(fakeCatalog.AddDetectedFaceCalls, Is.EqualTo(1));
            Assert.That(fakeCatalog.AddTagCalls, Is.EqualTo(0));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    private sealed class FakeRecognitionClient : IFaceRecognitionClient
    {
        public Task<FaceAnalysisResult> AnalyzeAsync(string imagePath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new FaceAnalysisResult
            {
                ModelName = "mock",
                Faces = new List<FaceDetectionResult>
                {
                    new()
                    {
                        X = 1,
                        Y = 2,
                        Width = 3,
                        Height = 4,
                        Confidence = 0.99,
                        Embedding = new[] { 0.11f, 0.22f, 0.33f }
                    }
                }
            });
        }
    }

    private sealed class FakeCatalogService : IFaceCatalogService
    {
        public int AddDetectedFaceCalls { get; private set; }
        public int AddTagCalls { get; private set; }
        private int _photoId = 1;

        public Task ClearUnconfirmedFacesForPhotoAsync(int photoAssetId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ClearAllFacesForPhotoAsync(int photoAssetId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<DetectedFace> AddDetectedFaceAsync(int photoAssetId, double x, double y, double width, double height, double confidence, byte[] embeddingVector, string modelName, CancellationToken cancellationToken = default)
        {
            AddDetectedFaceCalls++;
            return Task.FromResult(new DetectedFace { Id = AddDetectedFaceCalls, PhotoAssetId = photoAssetId });
        }

        public Task AddTagToPhotoAsync(int photoAssetId, string tagName, TagKind kind, double? confidence = null, string source = "system", CancellationToken cancellationToken = default)
        {
            AddTagCalls++;
            return Task.CompletedTask;
        }

        public Task AssignFaceToPersonAsync(int detectedFaceId, int personId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<FacePerson> CreatePersonAsync(string displayName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new FacePerson { Id = 1, DisplayName = displayName });
        }

        public Task RenamePersonAsync(int personId, string newDisplayName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task MergePersonsAsync(int sourcePersonId, int targetPersonId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FacePerson>> GetPersonsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<FacePerson>>(new List<FacePerson>());
        }

        public Task<IReadOnlyList<DetectedFace>> GetUnknownFacesAsync(int take = 100, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteDetectedFaceAsync(int detectedFaceId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DetectedFace>> GetFacesByPersonAsync(int personId, int take = 200, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeletePersonAsync(int personId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DetectedFace>> GetConfirmedFacesWithEmbeddingsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DetectedFace>>(Array.Empty<DetectedFace>());
        public Task ResetCatalogAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DetectedFace>> GetFacesForPhotoAsync(int photoAssetId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DetectedFace>>(Array.Empty<DetectedFace>());
        public Task<int> GetAllPhotosCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> GetAllFacesCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> GetPersonsCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> GetAllTagsCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task DeleteAllPhotoTagsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAllDetectedFacesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAllPhotosAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;


        public Task<PhotoAsset> UpsertPhotoAsync(string filePath, string? fileHash, DateTime? capturedAtUtc, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PhotoAsset { Id = _photoId++, FilePath = filePath, FileHash = fileHash, CapturedAtUtc = capturedAtUtc });
        }

        public Task<PhotoAsset?> FindPhotoByHashAsync(string fileHash, CancellationToken cancellationToken = default)
            => Task.FromResult<PhotoAsset?>(null);

        public Task<bool> PhotoHasFacesAsync(int photoAssetId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }
}
