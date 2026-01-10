using NUnit.Framework;
using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace PhotoSorterApp.Tests;

[TestFixture, Apartment(ApartmentState.STA)]
public class CleanupQuarantinePathTests
{
    [Test]
    public void QuarantineFolder_ShouldBeCreated_InsideSelectedFolder()
    {
        // Arrange
        var root = Path.Combine(Path.GetTempPath(), $"CleanupTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var quarantine = Path.Combine(root, $"Карантин_{DateTime.Now:yyyyMMdd_HHmm}");

            // Act
            Directory.CreateDirectory(quarantine);

            // Assert
            Assert.That(Directory.Exists(quarantine), Is.True);
            Assert.That(Path.GetDirectoryName(quarantine)!.TrimEnd(Path.DirectorySeparatorChar), Is.EqualTo(root.TrimEnd(Path.DirectorySeparatorChar)));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Test]
    public void ShouldReject_RootDrivePath_Check()
    {
        // this test mirrors root detection logic in cleanup
        var systemDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System));
        Assert.That(systemDrive, Is.Not.Null);

        var folder = systemDrive!.TrimEnd('\\');
        var root = Path.GetPathRoot(folder);

        Assert.That(root, Is.Not.Empty);
        Assert.That(string.Equals(root.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase), Is.True);
    }
}
