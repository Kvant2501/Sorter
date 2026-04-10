using PhotoSorterApp.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<GalleryRepository>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/persons", (GalleryRepository repo) => Results.Ok(repo.GetPersons()));
app.MapGet("/api/tags", (GalleryRepository repo) => Results.Ok(repo.GetTags()));
app.MapGet("/api/folders", (GalleryRepository repo) => Results.Ok(repo.GetFolders()));
app.MapGet("/api/archive", (GalleryRepository repo) => Results.Ok(repo.GetArchiveMonths()));
app.MapGet("/api/albums", (GalleryRepository repo) => Results.Ok(repo.GetAlbums()));

app.MapGet("/api/photos", (GalleryRepository repo, string? person, string? tag, string? folder, int? year, int? month, int skip = 0, int take = 60) =>
{
    take = Math.Clamp(take, 1, 200);
    skip = Math.Max(0, skip);
    return Results.Ok(repo.GetPhotos(person, tag, folder, year, month, skip, take));
});

app.MapGet("/api/random-selection", (GalleryRepository repo, string? person, string? tag, string? folder, int? year, int? month, int count = 20) =>
{
    count = Math.Clamp(count, 1, 100);
    return Results.Ok(repo.GetRandomSelection(count, person, tag, folder, year, month));
});

app.MapGet("/api/image", (string path) =>
{
    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        return Results.NotFound();

    var ext = Path.GetExtension(path).ToLowerInvariant();
    var contentType = ext switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".bmp" => "image/bmp",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => "application/octet-stream"
    };

    return Results.File(path, contentType);
});

app.Run();
