#nullable enable

using Microsoft.EntityFrameworkCore;
using System;
using System.IO;

namespace PhotoSorterApp.Services;

public static class FaceCatalogDatabase
{
    private static readonly string DataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhotoSorter");
    private static readonly string DatabasePath = Path.Combine(DataFolder, "face-catalog.db");

    public static string CurrentDatabasePath => DatabasePath;

    public static FaceCatalogDbContext CreateContext()
    {
        if (!Directory.Exists(DataFolder))
            Directory.CreateDirectory(DataFolder);

        var options = new DbContextOptionsBuilder<FaceCatalogDbContext>()
            .UseSqlite($"Data Source={DatabasePath}")
            .EnableSensitiveDataLogging(false)
            .Options;

        return new FaceCatalogDbContext(options);
    }

    public static void EnsureCreated()
    {
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }
}
