#nullable enable

using PhotoSorterApp.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoSorterApp.Services;

public class FaceLabelingService
{
    private readonly IFaceCatalogService _catalogService;

    public FaceLabelingService(IFaceCatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    public async Task<FacePerson> EnsurePersonAsync(string displayName, CancellationToken cancellationToken = default)
    {
        var normalized = displayName.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Имя не может быть пустым.", nameof(displayName));

        var people = await _catalogService.GetPersonsAsync(cancellationToken);
        var existing = people.FirstOrDefault(x => string.Equals(x.DisplayName, normalized, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return existing;

        return await _catalogService.CreatePersonAsync(normalized, cancellationToken);
    }

    public async Task<int> AssignFacesToPersonAsync(IEnumerable<DetectedFace> faces, string personName, CancellationToken cancellationToken = default)
    {
        var person = await EnsurePersonAsync(personName, cancellationToken);
        var assigned = 0;

        foreach (var face in faces)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _catalogService.AssignFaceToPersonAsync(face.Id, person.Id, cancellationToken);
            await _catalogService.AddTagToPhotoAsync(face.PhotoAssetId, person.DisplayName, TagKind.Person, face.Confidence, "user-confirmation", cancellationToken);
            assigned++;
        }

        return assigned;
    }
}
