using System;
using NUnit.Framework;
using System.Threading;
using System.Windows;

namespace PhotoSorterApp.Tests;

[SetUpFixture]
public class WpfTestApplicationFixture
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        if (Application.Current == null)
        {
            _ = new Application();
        }

        EnsureDict("Themes/Colors.Light.xaml");
        EnsureDict("Themes/Brushes.xaml");
        EnsureDict("Styles/Animations.xaml");
        EnsureDict("Styles/Controls.xaml");
    }

    private static void EnsureDict(string packUriContains)
    {
        var app = Application.Current!;
        foreach (var d in app.Resources.MergedDictionaries)
        {
            if (d.Source?.OriginalString.IndexOf(packUriContains, StringComparison.OrdinalIgnoreCase) >= 0)
                return;
        }

        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/PhotoSorterApp;component/{packUriContains}", UriKind.Absolute)
        });
    }
}
