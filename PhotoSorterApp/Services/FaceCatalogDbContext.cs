#nullable enable

using Microsoft.EntityFrameworkCore;
using PhotoSorterApp.Models;

namespace PhotoSorterApp.Services;

public class FaceCatalogDbContext : DbContext
{
    public FaceCatalogDbContext(DbContextOptions<FaceCatalogDbContext> options) : base(options)
    {
    }

    public DbSet<PhotoAsset> PhotoAssets => Set<PhotoAsset>();
    public DbSet<DetectedFace> DetectedFaces => Set<DetectedFace>();
    public DbSet<FacePerson> FacePersons => Set<FacePerson>();
    public DbSet<FaceEmbedding> FaceEmbeddings => Set<FaceEmbedding>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<PhotoTag> PhotoTags => Set<PhotoTag>();
    public DbSet<PhotoAlbum> PhotoAlbums => Set<PhotoAlbum>();
    public DbSet<AlbumPhoto> AlbumPhotos => Set<AlbumPhoto>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PhotoAsset>()
            .HasIndex(x => x.FilePath)
            .IsUnique();

        modelBuilder.Entity<Tag>()
            .HasIndex(x => new { x.Kind, x.Name })
            .IsUnique();

        modelBuilder.Entity<PhotoTag>()
            .HasKey(x => new { x.PhotoAssetId, x.TagId });

        modelBuilder.Entity<AlbumPhoto>()
            .HasKey(x => new { x.PhotoAlbumId, x.PhotoAssetId });

        modelBuilder.Entity<DetectedFace>()
            .HasOne(x => x.FaceEmbedding)
            .WithOne(x => x.Face)
            .HasForeignKey<DetectedFace>(x => x.FaceEmbeddingId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<DetectedFace>()
            .HasOne(x => x.ConfirmedPerson)
            .WithMany(x => x.ConfirmedFaces)
            .HasForeignKey(x => x.ConfirmedPersonId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<DetectedFace>()
            .HasIndex(x => x.PhotoAssetId);

        modelBuilder.Entity<DetectedFace>()
            .HasIndex(x => x.ConfirmedPersonId);
    }
}
