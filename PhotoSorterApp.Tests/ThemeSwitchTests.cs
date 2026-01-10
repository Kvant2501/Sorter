using NUnit.Framework;
using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace PhotoSorterApp.Tests;

[TestFixture, Apartment(ApartmentState.STA)]
public class ThemeSwitchTests
{
    [Test]
    public void ApplyTheme_SwitchesColorsDictionary_InApplicationResources()
    {
        var app = Application.Current!;

        // call real code on existing App instance if available
        if (Application.Current is PhotoSorterApp.App psApp)
        {
            psApp.ApplyTheme("Light");
            Assert.AreEqual("Light", psApp.CurrentTheme);

            psApp.ApplyTheme("Dark");
            Assert.AreEqual("Dark", psApp.CurrentTheme);

            bool hasDark = psApp.Resources.MergedDictionaries.Any(d => d.Source?.OriginalString.Contains("Colors.Dark.xaml", StringComparison.OrdinalIgnoreCase) == true);
            Assert.IsTrue(hasDark);

            psApp.ApplyTheme("Light");
            bool hasLight = psApp.Resources.MergedDictionaries.Any(d => d.Source?.OriginalString.Contains("Colors.Light.xaml", StringComparison.OrdinalIgnoreCase) == true);
            Assert.IsTrue(hasLight);
        }
        else
        {
            Assert.Inconclusive("PhotoSorterApp.App is not available as Application.Current in this test host.");
        }
    }
}