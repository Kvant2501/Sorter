#nullable enable

using Microsoft.EntityFrameworkCore;
using PhotoSorterApp.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoSorterApp.Services;

/// <summary>
/// Repairs missing Person tags for already-confirmed faces.
/// When you have 173 faces with ConfirmedPersonId != null but no PhotoTags,
/// this creates those tags so web gallery and filters work.
/// </summary>
public class FaceTagSyncService
{
    public async Task<(int SyncedPhotos, int CreatedTags)> SyncPersonTagsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = FaceCatalogDatabase.CreateContext();

        // Find all ConfirmedPersonId != null without a corresponding Person tag
        var facesWithoutPersonTag = await db.DetectedFaces
            .AsNoTracking()
            .Where(df => df.ConfirmedPersonId != null)
            .Include(df => df.PhotoAsset)
            .ToListAsync(cancellationToken);

        int syncedPhotos = 0;
        int createdTags = 0;

        var personCache = new Dictionary<int, FacePerson>();
        var tagCache = new Dictionary<int, Tag>();

        foreach (var face in facesWithoutPersonTag)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var personId = face.ConfirmedPersonId.Value;
            
            if (!personCache.TryGetValue(personId, out var person))
            {
                person = await db.FacePersons.AsNoTracking().FirstAsync(p => p.Id == personId, cancellationToken);
                personCache[personId] = person;
            }

            // Get or create Person tag
            if (!tagCache.TryGetValue(personId, out var tag))
            {
                tag = await db.Tags.FirstOrDefaultAsync(
                    t => t.Name == person.DisplayName && t.Kind == TagKind.Person, 
                    cancellationToken);
                
                if (tag is null)
                {
                    tag = new Tag { Name = person.DisplayName, Kind = TagKind.Person };
                    db.Tags.Add(tag);
                    await db.SaveChangesAsync(cancellationToken);
                }
                tagCache[personId] = tag;
            }

            // Check if PhotoTag already exists
            var existingTag = await db.PhotoTags
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    pt => pt.PhotoAssetId == face.PhotoAssetId && pt.TagId == tag.Id,
                    cancellationToken);

            if (existingTag is null)
            {
                db.PhotoTags.Add(new PhotoTag
                {
                    PhotoAssetId = face.PhotoAssetId,
                    TagId = tag.Id,
                    Confidence = face.Confidence,
                    Source = "sync-tags"
                });
                createdTags++;

                if (createdTags % 50 == 0)
                    await db.SaveChangesAsync(cancellationToken);
            }

            syncedPhotos++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return (syncedPhotos, createdTags);
    }
}
