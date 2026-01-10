using NUnit.Framework;
using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace PhotoSorterApp.Tests;

[TestFixture, Apartment(ApartmentState.STA)]
public class ThemeSwitchAdditionalTests
{
    [Test]
    public void ApplyTheme_ReplacesOnlyOldColorsDictionary_AndKeepsBrushes()
    {
        if (Application.Current is not PhotoSorterApp.App app)
        {
            Assert.Inconclusive("PhotoSorterApp.App is not available as Application.Current in this test host.");
            return;
        }

        app.ApplyTheme("Light");
        app.ApplyTheme("Dark");

        var colorDicts = app.Resources.MergedDictionaries
            .Where(d => d.Source != null && d.Source.OriginalString.IndexOf("Colors.", StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();

        Assert.AreEqual(1, colorDicts.Count);
        Assert.IsTrue(colorDicts[0].Source!.OriginalString.Contains("Colors.Dark.xaml", StringComparison.OrdinalIgnoreCase));

        bool brushesPresent = app.Resources.MergedDictionaries.Any(d => d.Source != null && d.Source.OriginalString.Contains("Brushes.xaml", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(brushesPresent);
    }

    [Test]
    public void PrimaryBrush_IsAvailable_InMergedResourcesAfterApplyTheme()
    {
        if (Application.Current is not PhotoSorterApp.App app)
        {
            Assert.Inconclusive("PhotoSorterApp.App is not available as Application.Current in this test host.");
            return;
        }

        app.ApplyTheme("Dark");

        bool foundPrimaryBrush = app.Resources.MergedDictionaries.Any(d =>
        {
            try { return d.Contains("PrimaryBrush"); }
            catch { return false; }
        });

        Assert.IsTrue(foundPrimaryBrush);
    }
}
