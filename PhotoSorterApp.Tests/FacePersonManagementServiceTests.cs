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
public class FacePersonManagementServiceTests
{
    [Test]
    public async Task MergePersonIntoNameAsync_CreatesTarget_WhenMissing()
    {
        var fake = new FakeCatalogService
        {
            Persons = new List<FacePerson> { new() { Id = 1, DisplayName = "Alice" } }
        };

        var service = new FacePersonManagementService(fake);
        var target = await service.MergePersonIntoNameAsync(1, "Bob");

        Assert.That(target.DisplayName, Is.EqualTo("Bob"));
        Assert.That(fake.CreatePersonCalls, Is.EqualTo(1));
        Assert.That(fake.MergeCalls.Single(), Is.EqualTo((1, target.Id)));
    }

    [Test]
    public async Task RenamePersonAsync_DelegatesToCatalog()
    {
        var fake = new FakeCatalogService
        {
            Persons = new List<FacePerson> { new() { Id = 3, DisplayName = "Old" } }
        };

        var service = new FacePersonManagementService(fake);
        await service.RenamePersonAsync(3, "New");

        Assert.That(fake.Persons.Single().DisplayName, Is.EqualTo("New"));
    }

    private sealed class FakeCatalogService : IFaceCatalogService
    {
        private int _nextId = 10;
        public List<FacePerson> Persons { get; set; } = new();
        public int CreatePersonCalls { get; private set; }
        public List<(int source, int target)> MergeCalls { get; } = new();

        public Task<FacePerson> CreatePersonAsync(string displayName, CancellationToken cancellationToken = default)
        {
            CreatePersonCalls++;
            var p = new FacePerson { Id = _nextId++, DisplayName = displayName };
            Persons.Add(p);
            return Task.FromResult(p);
        }

        public Task<IReadOnlyList<FacePerson>> GetPersonsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FacePerson>>(Persons.ToList());

        public Task RenamePersonAsync(int personId, string newDisplayName, CancellationToken cancellationToken = default)
        {
            var person = Persons.First(x => x.Id == personId);
            person.DisplayName = newDisplayName;
            return Task.CompletedTask;
        }

        public Task MergePersonsAsync(int sourcePersonId, int targetPersonId, CancellationToken cancellationToken = default)
        {
            MergeCalls.Add((sourcePersonId, targetPersonId));
            var src = Persons.FirstOrDefault(x => x.Id == sourcePersonId);
            if (src is not null)
                Persons.Remove(src);
            return Task.CompletedTask;
        }

        public Task ClearUnconfirmedFacesForPhotoAsync(int photoAssetId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ClearAllFacesForPhotoAsync(int photoAssetId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PhotoAsset> UpsertPhotoAsync(string filePath, string? fileHash, DateTime? capturedAtUtc, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DetectedFace> AddDetectedFaceAsync(int photoAssetId, double x, double y, double width, double height, double confidence, byte[] embeddingVector, string modelName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task AssignFaceToPersonAsync(int detectedFaceId, int personId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task AddTagToPhotoAsync(int photoAssetId, string tagName, TagKind kind, double? confidence = null, string source = "system", CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DetectedFace>> GetUnknownFacesAsync(int take = 100, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteDetectedFaceAsync(int detectedFaceId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DetectedFace>> GetFacesByPersonAsync(int personId, int take = 200, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeletePersonAsync(int personId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DetectedFace>> GetConfirmedFacesWithEmbeddingsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ResetCatalogAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PhotoAsset?> FindPhotoByHashAsync(string fileHash, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> PhotoHasFacesAsync(int photoAssetId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DetectedFace>> GetFacesForPhotoAsync(int photoAssetId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> GetAllPhotosCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> GetAllFacesCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> GetPersonsCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(Persons.Count);
        public Task<int> GetAllTagsCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task DeleteAllPhotoTagsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAllDetectedFacesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAllPhotosAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
