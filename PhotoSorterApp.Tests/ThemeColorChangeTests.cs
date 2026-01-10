using NUnit.Framework;
using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;

namespace PhotoSorterApp.Tests;

[TestFixture, Apartment(ApartmentState.STA)]
public class ThemeColorChangeTests
{
    private static PhotoSorterApp.App GetAppOrInconclusive()
    {
        if (Application.Current is PhotoSorterApp.App app)
            return app;

        Assert.Inconclusive("PhotoSorterApp.App is not available as Application.Current in this test host.");
        throw new InvalidOperationException();
    }

    private static ResourceDictionary? FindColorsDictionary(PhotoSorterApp.App app, string nameContains)
    {
        return app.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Test]
    public void PrimaryColor_Changes_When_Switching_Themes()
    {
        var app = GetAppOrInconclusive();

        app.ApplyTheme("Light");
        var lightDict = FindColorsDictionary(app, "Colors.Light.xaml");
        Assert.IsNotNull(lightDict);
        var lightColor = (Color)lightDict!["PrimaryColor"]!;

        app.ApplyTheme("Dark");
        var darkDict = FindColorsDictionary(app, "Colors.Dark.xaml");
        Assert.IsNotNull(darkDict);
        var darkColor = (Color)darkDict!["PrimaryColor"]!;

        Assert.AreNotEqual(lightColor, darkColor);
    }

    [Test]
    public void PrimaryBrush_Reflects_PrimaryColor_When_Switching_Themes()
    {
        var app = GetAppOrInconclusive();

        app.ApplyTheme("Light");
        var brushLight = app.Resources["PrimaryBrush"] as SolidColorBrush;
        Assert.IsNotNull(brushLight);

        app.ApplyTheme("Dark");
        var brushDark = app.Resources["PrimaryBrush"] as SolidColorBrush;
        Assert.IsNotNull(brushDark);

        Assert.AreNotEqual(brushLight!.Color, brushDark!.Color);
    }
}
