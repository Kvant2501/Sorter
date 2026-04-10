#nullable enable

using Microsoft.EntityFrameworkCore;
using PhotoSorterApp.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoSorterApp.Services;

public class FaceCatalogService : IFaceCatalogService
{
    public async Task<PhotoAsset> UpsertPhotoAsync(string filePath, string? fileHash, DateTime? capturedAtUtc, CancellationToken cancellationToken = default)
    {
        await using var db = FaceCatalogDatabase.CreateContext();

        var existing = await db.PhotoAssets.FirstOrDefaultAsync(x => x.FilePath == filePath, cancellationToken);
        if (existing is not null)
        {
            existing.FileHash = fileHash;
            existing.CapturedAtUtc = capturedAtUtc;
            await db.SaveChangesAsync(cancellationToken);
            return existing;
        }

        var photo = new PhotoAsset
        {
            FilePath = filePath,
            FileHash = fileHash,
            CapturedAtUtc = capturedAtUtc
        };

        db.PhotoAssets.Add(photo);
        await db.SaveChangesAsync(cancellationToken);
        return photo;
    }

    public async Task<FacePerson> CreatePersonAsync(string displayName, CancellationToken cancellationToken = default)
    {
        var normalizedName = displayName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
            throw new ArgumentException("Person name is required.", nameof(displayName));

        await using var db = FaceCatalogDatabase.CreateContext();
        var person = new FacePerson
        {
            DisplayName = normalizedName
        };

        db.FacePersons.Add(person);
        await db.SaveChangesAsync(cancellationToken);
        return person;
    }

    public async Task<IReadOnlyList<FacePerson>> GetPersonsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = FaceCatalogDatabase.CreateContext();

        var persons = await db.FacePersons.AsNoTracking().ToListAsync(cancellationToken);
        var confirmedCounts = await db.DetectedFaces
            .AsNoTracking()
            .Where(x => x.ConfirmedPersonId != null)
            .GroupBy(x => x.ConfirmedPersonId!.Value)
            .Select(g => new { PersonId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PersonId, x => x.Count, cancellationToken);

        foreach (var p in persons)
            p.FaceCount = confirmedCounts.TryGetValue(p.Id, out var c) ? c : 0;

        return persons;
    }

    public async Task RenamePersonAsync(int personId, string newDisplayName, CancellationToken cancellationToken = default)
    {
        var normalized = newDisplayName.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Person name is required.", nameof(newDisplayName));

        await using var db = FaceCatalogDatabase.CreateContext();
        var person = await db.FacePersons.FirstOrDefaultAsync(x => x.Id == personId, cancellationToken)
            ?? throw new InvalidOperationException($"Person #{personId} was not found.");

        var duplicateExists = await db.FacePersons.AnyAsync(x => x.Id != personId && x.DisplayName.ToLower() == normalized.ToLower(), cancellationToken);
        if (duplicateExists)
            throw new InvalidOperationException($"Person with name '{normalized}' already exists.");

        var oldName = person.DisplayName;
        person.DisplayName = normalized;

        var oldTag = await db.Tags.FirstOrDefaultAsync(x => x.Kind == TagKind.Person && x.Name == oldName, cancellationToken);
        if (oldTag is not null)
        {
            var existingNewTag = await db.Tags.FirstOrDefaultAsync(x => x.Kind == TagKind.Person && x.Name == normalized, cancellationToken);
            if (existingNewTag is null)
            {
                oldTag.Name = normalized;
            }
            else
            {
                var oldLinks = await db.PhotoTags.Where(x => x.TagId == oldTag.Id).ToListAsync(cancellationToken);
                foreach (var link in oldLinks)
                {
                    var exists = await db.PhotoTags.AnyAsync(x => x.PhotoAssetId == link.PhotoAssetId && x.TagId == existingNewTag.Id, cancellationToken);
                    if (!exists)
                    {
                        db.PhotoTags.Add(new PhotoTag
                        {
                            PhotoAssetId = link.PhotoAssetId,
                            TagId = existingNewTag.Id,
                            Confidence = link.Confidence,
                            Source = link.Source
                        });
                    }

                    db.PhotoTags.Remove(link);
                }

                db.Tags.Remove(oldTag);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MergePersonsAsync(int sourcePersonId, int targetPersonId, CancellationToken cancellationToken = default)
    {
        if (sourcePersonId == targetPersonId)
            throw new ArgumentException("Source and target persons must be different.");

        await using var db = FaceCatalogDatabase.CreateContext();

        var source = await db.FacePersons.FirstOrDefaultAsync(x => x.Id == sourcePersonId, cancellationToken)
            ?? throw new InvalidOperationException($"Person #{sourcePersonId} was not found.");
        var target = await db.FacePersons.FirstOrDefaultAsync(x => x.Id == targetPersonId, cancellationToken)
            ?? throw new InvalidOperationException($"Person #{targetPersonId} was not found.");

        var faces = await db.DetectedFaces.Where(x => x.ConfirmedPersonId == sourcePersonId).ToListAsync(cancellationToken);
        foreach (var face in faces)
            face.ConfirmedPersonId = targetPersonId;

        var sourceTag = await db.Tags.FirstOrDefaultAsync(x => x.Kind == TagKind.Person && x.Name == source.DisplayName, cancellationToken);
        var targetTag = await db.Tags.FirstOrDefaultAsync(x => x.Kind == TagKind.Person && x.Name == target.DisplayName, cancellationToken);

        if (sourceTag is not null)
        {
            if (targetTag is null)
            {
                sourceTag.Name = target.DisplayName;
            }
            else
            {
                var links = await db.PhotoTags.Where(x => x.TagId == sourceTag.Id).ToListAsync(cancellationToken);
                foreach (var link in links)
                {
                    var exists = await db.PhotoTags.AnyAsync(x => x.PhotoAssetId == link.PhotoAssetId && x.TagId == targetTag.Id, cancellationToken);
                    if (!exists)
                    {
                        db.PhotoTags.Add(new PhotoTag
                        {
                            PhotoAssetId = link.PhotoAssetId,
                            TagId = targetTag.Id,
                            Confidence = link.Confidence,
                            Source = link.Source
                        });
                    }

                    db.PhotoTags.Remove(link);
                }

                db.Tags.Remove(sourceTag);
            }
        }

        target.LastConfirmedUtc = DateTime.UtcNow;
        db.FacePersons.Remove(source);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<DetectedFace> AddDetectedFaceAsync(
        int photoAssetId,
        double x,
        double y,
        double width,
        double height,
        double confidence,
        byte[] embeddingVector,
        string modelName,
        CancellationToken cancellationToken = default)
    {
        await using var db = FaceCatalogDatabase.CreateContext();

        var embedding = new FaceEmbedding
        {
            ModelName = modelName,
            Dimension = embeddingVector.Length,
            Vector = embeddingVector
        };

        var face = new DetectedFace
        {
            PhotoAssetId = photoAssetId,
            BoundingBoxX = x,
            BoundingBoxY = y,
            BoundingBoxWidth = width,
            BoundingBoxHeight = height,
            Confidence = confidence,
            FaceEmbedding = embedding
        };

        db.DetectedFaces.Add(face);
        await db.SaveChangesAsync(cancellationToken);
        return face;
    }

    public async Task AssignFaceToPersonAsync(int detectedFaceId, int personId, CancellationToken cancellationToken = default)
    {
        await using var db = FaceCatalogDatabase.CreateContext();
        var face = await db.DetectedFaces
            .Include(x => x.PhotoAsset)
            .FirstOrDefaultAsync(x => x.Id == detectedFaceId, cancellationToken)
            ?? throw new InvalidOperationException($"Detected face #{detectedFaceId} was not found.");

        var person = await db.FacePersons.FirstOrDefaultAsync(x => x.Id == personId, cancellationToken)
            ?? throw new InvalidOperationException($"Person #{personId} was not found.");

        face.ConfirmedPersonId = personId;
        person.LastConfirmedUtc = DateTime.UtcNow;

        // Keep tags consistent even if caller did not add a tag.
        var normalizedName = person.DisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedName))
        {
            var tag = await db.Tags.FirstOrDefaultAsync(x => x.Name == normalizedName && x.Kind == TagKind.Person, cancellationToken);
            if (tag is null)
            {
                tag = new Tag { Name = normalizedName, Kind = TagKind.Person };
                db.Tags.Add(tag);
                await db.SaveChangesAsync(cancellationToken);
            }

            var exists = await db.PhotoTags.AnyAsync(x => x.PhotoAssetId == face.PhotoAssetId && x.TagId == tag.Id, cancellationToken);
            if (!exists)
            {
                db.PhotoTags.Add(new PhotoTag
                {
                    PhotoAssetId = face.PhotoAssetId,
                    TagId = tag.Id,
                    Confidence = face.Confidence,
                    Source = "user-confirmation"
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DetectedFace>> GetUnknownFacesAsync(int take = 100, CancellationToken cancellationToken = default)
    {
        await using var db = FaceCatalogDatabase.CreateContext();

        return await db.DetectedFaces
            .AsNoTracking()
            .Include(x => x.PhotoAsset)
            .Include(x => x.FaceEmbedding)
            .Where(x => x.ConfirmedPersonId == null)
            .OrderByDescending(x => x.Confidence)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task ClearUnconfirmedFacesForPhotoAsync(int photoAssetId, CancellationToken cancellationToken = default)
    {
        await using var db = FaceCatalogDatabase.CreateContext();

        var staleFaces = await db.DetectedFaces
            .Where(x => x.PhotoAssetId == photoAssetId && x.ConfirmedPersonId == null)
            .ToListAsync(cancellationToken);

        if (staleFaces.Count > 0)
            db.DetectedFaces.RemoveRange(staleFaces);

        var staleTags = await db.PhotoTags
            .Include(x => x.Tag)
            .Where(x => x.PhotoAssetId == photoAssetId && x.Source == "face-api" && x.Tag.Kind != TagKind.Person)
            .ToListAsync(cancellationToken);

        if (staleTags.Count > 0)
            db.PhotoTags.RemoveRange(staleTags);

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearAllFacesForPhotoAsync(int photoAssetId, CancellationToken cancellationToken = default)
    {
        await using var db = FaceCatalogDatabase.CreateContext();

        var allFaces = await db.DetectedFaces
            .Where(x => x.PhotoAssetId == photoAssetId)
            .ToListAsync(cancellationToken);

        if (allFaces.Count > 0)
            db.DetectedFaces.RemoveRange(allFaces);

        var staleTags = await db.PhotoTags
            .Include(x => x.Tag)
            .Where(x => x.PhotoAssetId == photoAssetId && (x.Source == "face-api" || x.Source == "auto-recognition"))
            .ToListAsync(cancellationToken);

        if (staleTags.Count > 0)
            db.PhotoTags.RemoveRange(staleTags);

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteDetectedFaceAsync(int detectedFaceId, CancellationToken cancellationToken = default)
    {
        await using var db = FaceCatalogDatabase.CreateContext();
        var face = await db.DetectedFaces.FindAsync([detectedFaceId], cancellationToken);
        if (face is not null)
        {
            db.DetectedFaces.Remove(face);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<DetectedFace>> GetFacesByPersonAsync(int personId, int take = 200, CancellationToken cancellationToken = default)
    {
        await using var db = FaceCatalogDatabase.CreateContext();
        return await db.DetectedFaces
            .AsNoTracking()
            .Include(x => x.PhotoAsset)
            .Include(x => x.FaceEmbedding)
            .Where(x => x.ConfirmedPersonId == personId)
            .OrderByDescending(x => x.Confidence)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task DeletePersonAsync(int personId, CancellationToken cancellationToken = default)
    {
        await using var db = FaceCatalogDatabase.CreateContext();
        // Unlink faces first
        var faces = await db.DetectedFaces.Where(x => x.ConfirmedPersonId == personId).ToListAsync(cancellationToken);
        foreach (var f in faces)
            f.ConfirmedPersonId = null;

        var person = await db.FacePersons.FindAsync([personId], cancellationToken);
        if (person is not null)
            db.FacePersons.Remove(person);

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DetectedFace>> GetConfirmedFacesWithEmbeddingsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = FaceCatalogDatabase.CreateContext();
        return await db.DetectedFaces
            .AsNoTracking()
            .Include(x => x.FaceEmbedding)
            .Where(x => x.ConfirmedPersonId != null && x.FaceEmbedding != null && x.FaceEmbedding.Vector.Length > 0)
            .ToListAsync(cancellationToken);
    }

    public async Task ResetCatalogAsync(CancellationToken cancellationToken = default)
    {
        await using var db = FaceCatalogDatabase.CreateContext();
        await db.Database.EnsureDeletedAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);
    }

    public async Task<PhotoAsset?> FindPhotoByHashAsync(string fileHash, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileHash))
            return null;

        await using var db = FaceCatalogDatabase.CreateContext();
        return await db.PhotoAssets.AsNoTracking().FirstOrDefaultAsync(x => x.FileHash == fileHash, cancellationToken);
    }

    public async Task<bool> PhotoHasFacesAsync(int photoAssetId, CancellationToken cancellationToken = default)
    {
        await using var db = FaceCatalogDatabase.CreateContext();
        return await db.DetectedFaces.AsNoTracking().AnyAsync(x => x.PhotoAssetId == photoAssetId, cancellationToken);
    }

    public async Task<int> GetAllPhotosCountAsync(CancellationToken cancellationToken = default)
    {
        await using var db = FaceCatalogDatabase.CreateContext();
        return await db.PhotoAssets.CountAsync(cancellationToken);
    }

    public async Task<int> GetAllFacesCountAsync(CancellationToken cancellationToken = default)
    {
        await using var db = FaceCatalogDatabase.CreateContext();
        return await db.DetectedFaces.CountAsync(cancellationToken);
    }

    public async Task<int> GetPersonsCountAsync(CancellationToken cancellationToken = default)
    {
        await using var db = FaceCatalogDatabase.CreateContext();
        return await db.FacePersons.CountAsync(cancellationToken);
    }

    public async Task<int> GetAllTagsCountAsync(CancellationToken cancellationToken = default)
    {
        await using var db = FaceCatalogDatabase.CreateContext();
        return await db.PhotoTags.CountAsync(cancellationToken);
    }

    public async Task DeleteAllPhotoTagsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = FaceCatalogDatabase.CreateContext();
        await db.PhotoTags.ExecuteDeleteAsync(cancellationToken);
    }

    public async Task DeleteAllDetectedFacesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = FaceCatalogDatabase.CreateContext();
        await db.DetectedFaces.ExecuteDeleteAsync(cancellationToken);
    }

    public async Task DeleteAllPhotosAsync(CancellationToken cancellationToken = default)
    {
        await using var db = FaceCatalogDatabase.CreateContext();
        await db.PhotoAssets.ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DetectedFace>> GetFacesForPhotoAsync(int photoAssetId, CancellationToken cancellationToken = default)
    {
        await using var db = FaceCatalogDatabase.CreateContext();
        return await db.DetectedFaces
            .AsNoTracking()
            .Where(x => x.PhotoAssetId == photoAssetId)
            .ToListAsync(cancellationToken);
    }

    public async Task AddTagToPhotoAsync(int photoAssetId, string tagName, TagKind kind, double? confidence = null, string source = "system", CancellationToken cancellationToken = default)
    {
        var normalizedTag = tagName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedTag))
            throw new ArgumentException("Tag name is required.", nameof(tagName));

        await using var db = FaceCatalogDatabase.CreateContext();

        var tag = await db.Tags.FirstOrDefaultAsync(x => x.Name == normalizedTag && x.Kind == kind, cancellationToken);
        if (tag is null)
        {
            tag = new Tag { Name = normalizedTag, Kind = kind };
            db.Tags.Add(tag);
            await db.SaveChangesAsync(cancellationToken);
        }

        var exists = await db.PhotoTags.AnyAsync(x => x.PhotoAssetId == photoAssetId && x.TagId == tag.Id, cancellationToken);
        if (!exists)
        {
            db.PhotoTags.Add(new PhotoTag
            {
                PhotoAssetId = photoAssetId,
                TagId = tag.Id,
                Confidence = confidence,
                Source = source
            });
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
