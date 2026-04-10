#nullable enable

using PhotoSorterApp.Models;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoSorterApp.Services;

public class FacePersonManagementService
{
    private readonly IFaceCatalogService _catalog;

    public FacePersonManagementService(IFaceCatalogService catalog)
    {
        _catalog = catalog;
    }

    public Task RenamePersonAsync(int personId, string newName, CancellationToken cancellationToken = default)
    {
        var normalized = newName.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Name is required.", nameof(newName));

        return _catalog.RenamePersonAsync(personId, normalized, cancellationToken);
    }

    public async Task<FacePerson> MergePersonIntoNameAsync(int sourcePersonId, string targetName, CancellationToken cancellationToken = default)
    {
        var normalized = targetName.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Name is required.", nameof(targetName));

        var people = await _catalog.GetPersonsAsync(cancellationToken);
        var target = people.FirstOrDefault(x => string.Equals(x.DisplayName, normalized, StringComparison.OrdinalIgnoreCase));
        target ??= await _catalog.CreatePersonAsync(normalized, cancellationToken);

        if (target.Id == sourcePersonId)
            throw new InvalidOperationException("Cannot merge person into itself.");

        await _catalog.MergePersonsAsync(sourcePersonId, target.Id, cancellationToken);
        return target;
    }
}
