using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using PhotoSorterApp.Models;
using PhotoSorterApp.Services;

namespace PhotoSorterApp.Tests;

[TestFixture]
public class FaceCatalogDbContextTests
{
    [Test]
    public void PhotoAsset_FilePath_MustBeUnique()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<FaceCatalogDbContext>()
            .UseSqlite(connection)
            .Options;

        using (var db = new FaceCatalogDbContext(options))
        {
            db.Database.EnsureCreated();
            db.PhotoAssets.Add(new PhotoAsset { FilePath = "C:/photos/a.jpg" });
            db.SaveChanges();
        }

        using (var db = new FaceCatalogDbContext(options))
        {
            db.PhotoAssets.Add(new PhotoAsset { FilePath = "C:/photos/a.jpg" });
            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }
    }

    [Test]
    public void PhotoTag_ManyToMany_Link_IsPersisted()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<FaceCatalogDbContext>()
            .UseSqlite(connection)
            .Options;

        using var db = new FaceCatalogDbContext(options);
        db.Database.EnsureCreated();

        var photo = new PhotoAsset { FilePath = "C:/photos/b.jpg" };
        var tag = new Tag { Name = "family", Kind = TagKind.User };

        db.PhotoAssets.Add(photo);
        db.Tags.Add(tag);
        db.SaveChanges();

        db.PhotoTags.Add(new PhotoTag
        {
            PhotoAssetId = photo.Id,
            TagId = tag.Id,
            Source = "test"
        });
        db.SaveChanges();

        var linked = db.PhotoTags.Count(x => x.PhotoAssetId == photo.Id && x.TagId == tag.Id);
        Assert.That(linked, Is.EqualTo(1));
    }
}
