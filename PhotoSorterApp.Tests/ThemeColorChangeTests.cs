using NUnit.Framework;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using PhotoSorterApp;

namespace PhotoSorterApp.Tests
{
    [TestFixture, Apartment(ApartmentState.STA)]
    public class ThemeColorChangeTests
    {
        private App _app;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (Application.Current is App existing)
                _app = existing;
            else
                _app = new App();
        }

        private ResourceDictionary? FindColorsDictionary(string nameContains)
        {
            return _app.Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.IndexOf(nameContains, System.StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private Color? GetPrimaryColorFromColorsDict(string colorsFileName)
        {
            var dict = FindColorsDictionary(colorsFileName);
            if (dict == null) return null;
            if (dict.Contains("PrimaryColor"))
            {
                return (Color)dict["PrimaryColor"];
            }
            return null;
        }

        private SolidColorBrush? GetPrimaryBrush()
        {
            // PrimaryBrush should be available in merged dictionaries (Brushes.xaml)
            foreach (var d in _app.Resources.MergedDictionaries)
            {
                try
                {
                    if (d.Contains("PrimaryBrush"))
                    {
                        return d["PrimaryBrush"] as SolidColorBrush ?? _app.Resources["PrimaryBrush"] as SolidColorBrush;
                    }
                }
                catch { }
            }

            // fallback to Application resources
            if (_app.Resources.Contains("PrimaryBrush"))
                return _app.Resources["PrimaryBrush"] as SolidColorBrush;

            return null;
        }

        [Test]
        public void PrimaryColor_Changes_When_Switching_Themes()
        {
            // Ensure light theme then read color
            _app.ApplyTheme("Light");
            var lightColor = GetPrimaryColorFromColorsDict("Colors.Light.xaml");
            Assert.IsNotNull(lightColor, "PrimaryColor should be present in Colors.Light.xaml");

            // Switch to dark
            _app.ApplyTheme("Dark");
            var darkColor = GetPrimaryColorFromColorsDict("Colors.Dark.xaml");
            Assert.IsNotNull(darkColor, "PrimaryColor should be present in Colors.Dark.xaml");

            Assert.AreNotEqual(lightColor, darkColor, "PrimaryColor should differ between Light and Dark themes");
        }

        [Test]
        public void PrimaryBrush_Reflects_PrimaryColor_When_Switching_Themes()
        {
            _app.ApplyTheme("Light");
            var brush = GetPrimaryBrush();
            Assert.IsNotNull(brush, "PrimaryBrush should be available after applying theme");
            var colorLight = brush!.Color;

            _app.ApplyTheme("Dark");
            // Re-fetch brush instance (it may be the same instance and updated, or a new instance)
            var brushAfter = GetPrimaryBrush();
            Assert.IsNotNull(brushAfter, "PrimaryBrush should be available after switching to dark theme");
            var colorDark = brushAfter!.Color;

            Assert.AreNotEqual(colorLight, colorDark, "PrimaryBrush color should change after switching themes");
        }
    }
}
