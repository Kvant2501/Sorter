using NUnit.Framework;
using PhotoSorterApp.Models;
using PhotoSorterApp.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoSorterApp.Tests;

[TestFixture]
public class FaceLabelingServiceTests
{
    [Test]
    public async Task EnsurePersonAsync_ReusesExistingPerson_CaseInsensitive()
    {
        var fake = new FakeCatalogService
        {
            Persons = new List<FacePerson>
            {
                new() { Id = 10, DisplayName = "Иван" }
            }
        };

        var service = new FaceLabelingService(fake);
        var person = await service.EnsurePersonAsync("иван");

        Assert.That(person.Id, Is.EqualTo(10));
        Assert.That(fake.CreatePersonCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task AssignFacesToPersonAsync_AssignsAndAddsPersonTags()
    {
        var fake = new FakeCatalogService();
        var service = new FaceLabelingService(fake);

        var faces = new[]
        {
            new DetectedFace { Id = 1, PhotoAssetId = 100, Confidence = 0.9 },
            new DetectedFace { Id = 2, PhotoAssetId = 101, Confidence = 0.8 }
        };

        var assigned = await service.AssignFacesToPersonAsync(faces, "Ольга");

        Assert.That(assigned, Is.EqualTo(2));
        Assert.That(fake.AssignCalls.Count, Is.EqualTo(2));
        Assert.That(fake.TagCalls.Count, Is.EqualTo(2));
        Assert.That(fake.Persons.Any(p => p.DisplayName == "Ольга"), Is.True);
    }

    private sealed class FakeCatalogService : IFaceCatalogService
    {
        private int _personId = 1000;
        public List<FacePerson> Persons { get; set; } = new();
        public List<DetectedFace> Faces { get; set; } = new();
        public int CreatePersonCalls { get; private set; }
        public List<(int faceId, int personId)> AssignCalls { get; } = new();
        public List<(int photoAssetId, string tag)> TagCalls { get; } = new();

        public Task ClearUnconfirmedFacesForPhotoAsync(int photoAssetId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearAllFacesForPhotoAsync(int photoAssetId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<PhotoAsset> UpsertPhotoAsync(string filePath, string? fileHash, DateTime? capturedAtUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(new PhotoAsset { Id = 1, FilePath = filePath, FileHash = fileHash, CapturedAtUtc = capturedAtUtc });

        public Task RenamePersonAsync(int personId, string newDisplayName, CancellationToken cancellationToken = default)
        {
            var person = Persons.FirstOrDefault(x => x.Id == personId) ?? throw new InvalidOperationException();
            person.DisplayName = newDisplayName;
            return Task.CompletedTask;
        }

        public Task MergePersonsAsync(int sourcePersonId, int targetPersonId, CancellationToken cancellationToken = default)
        {
            var source = Persons.FirstOrDefault(x => x.Id == sourcePersonId);
            if (source is not null)
                Persons.Remove(source);
            return Task.CompletedTask;
        }

        public Task<FacePerson> CreatePersonAsync(string displayName, CancellationToken cancellationToken = default)
        {
            CreatePersonCalls++;
            var person = new FacePerson { Id = _personId++, DisplayName = displayName };
            Persons.Add(person);
            return Task.FromResult(person);
        }

        public Task<IReadOnlyList<FacePerson>> GetPersonsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<FacePerson>>(Persons.ToList());
        }

        public Task AssignFaceToPersonAsync(int detectedFaceId, int personId, CancellationToken cancellationToken = default)
        {
            AssignCalls.Add((detectedFaceId, personId));
            return Task.CompletedTask;
        }

        public Task AddTagToPhotoAsync(int photoAssetId, string tagName, TagKind kind, double? confidence = null, string source = "system", CancellationToken cancellationToken = default)
        {
            TagCalls.Add((photoAssetId, tagName));
            return Task.CompletedTask;
        }

        public Task<DetectedFace> AddDetectedFaceAsync(int photoAssetId, double x, double y, double width, double height, double confidence, byte[] embeddingVector, string modelName, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<DetectedFace>> GetUnknownFacesAsync(int take = 100, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DetectedFace>>(Faces.Where(x => x.ConfirmedPersonId == null).Take(take).ToList());

        public Task DeleteDetectedFaceAsync(int detectedFaceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DetectedFace>> GetFacesByPersonAsync(int personId, int take = 200, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DetectedFace>>(Faces.Where(x => x.ConfirmedPersonId == personId).Take(take).ToList());
        public Task DeletePersonAsync(int personId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DetectedFace>> GetConfirmedFacesWithEmbeddingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DetectedFace>>(Faces.Where(x => x.ConfirmedPersonId != null && x.FaceEmbedding is not null).ToList());
        public Task ResetCatalogAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<PhotoAsset?> FindPhotoByHashAsync(string fileHash, CancellationToken cancellationToken = default)
            => Task.FromResult<PhotoAsset?>(null);

        public Task<bool> PhotoHasFacesAsync(int photoAssetId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<IReadOnlyList<DetectedFace>> GetFacesForPhotoAsync(int photoAssetId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DetectedFace>>(Faces.Where(x => x.PhotoAssetId == photoAssetId).ToList());

        public Task<int> GetAllPhotosCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> GetAllFacesCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(Faces.Count);
        public Task<int> GetPersonsCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(Persons.Count);
        public Task<int> GetAllTagsCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task DeleteAllPhotoTagsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAllDetectedFacesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAllPhotosAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
