using Microsoft.Data.Sqlite;
using NUnit.Framework;
using PhotoSorterApp.Web;
using System;
using System.IO;

namespace PhotoSorterApp.Tests;

[TestFixture]
public class GalleryRepositoryTests
{
    [Test]
    public void GetPhotos_AppliesPersonFilter()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "PhotoSorterTests", $"gallery-{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        try
        {
            using (var cn = new SqliteConnection($"Data Source={dbPath}"))
            {
                cn.Open();

                using var cmd = cn.CreateCommand();
                cmd.CommandText = """
                CREATE TABLE PhotoAssets (Id INTEGER PRIMARY KEY, FilePath TEXT NOT NULL, CapturedAtUtc TEXT NULL);
                CREATE TABLE Tags (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Kind INTEGER NOT NULL);
                CREATE TABLE PhotoTags (PhotoAssetId INTEGER NOT NULL, TagId INTEGER NOT NULL, Confidence REAL NULL, Source TEXT NULL);
                CREATE TABLE FacePersons (Id INTEGER PRIMARY KEY, DisplayName TEXT NOT NULL);
                CREATE TABLE DetectedFaces (Id INTEGER PRIMARY KEY, PhotoAssetId INTEGER NOT NULL, ConfirmedPersonId INTEGER NULL);
                CREATE TABLE PhotoAlbums (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, IsSmartAlbum INTEGER NOT NULL);
                CREATE TABLE AlbumPhotos (PhotoAlbumId INTEGER NOT NULL, PhotoAssetId INTEGER NOT NULL);
                INSERT INTO PhotoAssets (Id, FilePath) VALUES (1, 'c:/a.jpg'), (2, 'c:/b.jpg');
                INSERT INTO Tags (Id, Name, Kind) VALUES (1, 'Alice', 0), (2, 'Trip', 4);
                INSERT INTO PhotoTags (PhotoAssetId, TagId, Confidence, Source) VALUES (1, 1, 1.0, 'user'), (2, 2, 1.0, 'user');
                INSERT INTO FacePersons (Id, DisplayName) VALUES (1, 'Alice');
                INSERT INTO DetectedFaces (Id, PhotoAssetId, ConfirmedPersonId) VALUES (1, 1, 1);
                """;
                cmd.ExecuteNonQuery();
            }

            var repo = new GalleryRepository(dbPath);
            var all = repo.GetPhotos(null, null, 0, 10);
            var byPerson = repo.GetPhotos("Alice", null, 0, 10);

            Assert.That(all.Count, Is.EqualTo(2));
            Assert.That(byPerson.Count, Is.EqualTo(1));
            Assert.That(byPerson[0].FilePath, Is.EqualTo("c:/a.jpg"));
        }
        finally
        {
            try
            {
                if (File.Exists(dbPath))
                    File.Delete(dbPath);
            }
            catch
            {
                // ignore locked temp db cleanup
            }
        }
    }
}
