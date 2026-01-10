using System;
using System.IO;
using NUnit.Framework;
using PhotoSorterApp.Services;

namespace PhotoSorterApp.Tests;

[TestFixture]
public class DirectoryCatalogGeneratorTests
{
    [Test]
    public void GenerateCatalog_ShouldIncludeRootFiles()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), $"CatalogTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDir);
        
        try
        {
            // Create test files in root
            File.WriteAllText(Path.Combine(testDir, "file1.txt"), "test");
            File.WriteAllText(Path.Combine(testDir, "file2.jpg"), "test");
            
            // Act
            var catalog = DirectoryCatalogGenerator.GenerateCatalog(testDir, includeFiles: true, includeSize: true);
            
            // Assert
            Assert.That(catalog, Does.Contain("file1.txt"));
            Assert.That(catalog, Does.Contain("file2.jpg"));

            Assert.That(catalog, Does.Contain("<span class='file-icon'>TXT</span>"));
            Assert.That(catalog, Does.Contain("<span class='file-icon'>IMG</span>"));
        }
        finally
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }
    
    [Test]
    public void GenerateCatalog_ShouldIncludeSubfolderFiles()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), $"CatalogTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDir);
        
        try
        {
            // Create subfolder with files
            var subDir = Path.Combine(testDir, "SubFolder");
            Directory.CreateDirectory(subDir);
            File.WriteAllText(Path.Combine(subDir, "nested_file.txt"), "test");
            
            // Create deeper nesting
            var deepDir = Path.Combine(subDir, "DeepFolder");
            Directory.CreateDirectory(deepDir);
            File.WriteAllText(Path.Combine(deepDir, "deep_file.jpg"), "test");
            
            // Act
            var catalog = DirectoryCatalogGenerator.GenerateCatalog(testDir, includeFiles: true, includeSize: true);
            
            // Assert - should include subfolder
            Assert.That(catalog, Does.Contain("SubFolder"));
            Assert.That(catalog, Does.Contain("nested_file.txt"));
            
            // Assert - should include deep folder and file
            Assert.That(catalog, Does.Contain("DeepFolder"));
            Assert.That(catalog, Does.Contain("deep_file.jpg"));

            // should include count marker for files
            Assert.That(catalog, Does.Contain("<span class='folder-count'>"));
        }
        finally
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }
    
    [Test]
    public void GenerateCatalog_ShouldIncludeFolderCounts()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), $"CatalogTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDir);
        
        try
        {
            var subDir = Path.Combine(testDir, "TestFolder");
            Directory.CreateDirectory(subDir);
            
            // Create 3 files in subfolder
            File.WriteAllText(Path.Combine(subDir, "file1.txt"), "test");
            File.WriteAllText(Path.Combine(subDir, "file2.txt"), "test");
            File.WriteAllText(Path.Combine(subDir, "file3.txt"), "test");
            
            // Act
            var catalog = DirectoryCatalogGenerator.GenerateCatalog(testDir, includeFiles: true, includeSize: true);
            
            // Assert
            Assert.That(catalog, Does.Contain("TestFolder"));
            Assert.That(catalog, Does.Contain("3"));
            Assert.That(catalog, Does.Contain("folder-count"));
        }
        finally
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }
    
    [Test]
    public void GenerateCatalog_ShouldHandleEmptyFolders()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), $"CatalogTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDir);
        
        try
        {
            var emptyDir = Path.Combine(testDir, "EmptyFolder");
            Directory.CreateDirectory(emptyDir);
            
            // Act
            var catalog = DirectoryCatalogGenerator.GenerateCatalog(testDir, includeFiles: true, includeSize: true);
            
            // Assert - empty folder should appear but without content
            Assert.That(catalog, Does.Contain("EmptyFolder"));
        }
        finally
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }
}
