#nullable enable

using PhotoSorterApp.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoSorterApp.Services;

public interface IFaceCatalogService
{
    Task<PhotoAsset> UpsertPhotoAsync(string filePath, string? fileHash, DateTime? capturedAtUtc, CancellationToken cancellationToken = default);
    Task<PhotoAsset?> FindPhotoByHashAsync(string fileHash, CancellationToken cancellationToken = default);
    Task<bool> PhotoHasFacesAsync(int photoAssetId, CancellationToken cancellationToken = default);
    Task ClearUnconfirmedFacesForPhotoAsync(int photoAssetId, CancellationToken cancellationToken = default);
    Task ClearAllFacesForPhotoAsync(int photoAssetId, CancellationToken cancellationToken = default);
    Task<FacePerson> CreatePersonAsync(string displayName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FacePerson>> GetPersonsAsync(CancellationToken cancellationToken = default);
    Task RenamePersonAsync(int personId, string newDisplayName, CancellationToken cancellationToken = default);
    Task MergePersonsAsync(int sourcePersonId, int targetPersonId, CancellationToken cancellationToken = default);
    Task<DetectedFace> AddDetectedFaceAsync(int photoAssetId, double x, double y, double width, double height, double confidence, byte[] embeddingVector, string modelName, CancellationToken cancellationToken = default);
    Task AssignFaceToPersonAsync(int detectedFaceId, int personId, CancellationToken cancellationToken = default);
    Task AddTagToPhotoAsync(int photoAssetId, string tagName, TagKind kind, double? confidence = null, string source = "system", CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DetectedFace>> GetFacesForPhotoAsync(int photoAssetId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DetectedFace>> GetUnknownFacesAsync(int take = 100, CancellationToken cancellationToken = default);
    Task DeleteDetectedFaceAsync(int detectedFaceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DetectedFace>> GetFacesByPersonAsync(int personId, int take = 200, CancellationToken cancellationToken = default);
    Task DeletePersonAsync(int personId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DetectedFace>> GetConfirmedFacesWithEmbeddingsAsync(CancellationToken cancellationToken = default);
    Task ResetCatalogAsync(CancellationToken cancellationToken = default);
    
    // === Методы для диагностики и очистки ===
    Task<int> GetAllPhotosCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetAllFacesCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetPersonsCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetAllTagsCountAsync(CancellationToken cancellationToken = default);
    Task DeleteAllPhotoTagsAsync(CancellationToken cancellationToken = default);
    Task DeleteAllDetectedFacesAsync(CancellationToken cancellationToken = default);
    Task DeleteAllPhotosAsync(CancellationToken cancellationToken = default);
}
