using NUnit.Framework;
using PhotoSorterApp;
using System.Linq;
using System.Threading;
using System.Windows;

namespace PhotoSorterApp.Tests
{
    [TestFixture, Apartment(ApartmentState.STA)]
    public class ThemeSwitchAdditionalTests
    {
        private App _app;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (Application.Current is App existing)
            {
                _app = existing;
            }
            else
            {
                _app = new App();
            }
        }

        [Test]
        public void ApplyTheme_ReplacesOnlyOldColorsDictionary_AndKeepsBrushes()
        {
            var app = _app;

            // ensure starting state
            app.ApplyTheme("Light");
            // apply dark
            app.ApplyTheme("Dark");

            var colorDicts = app.Resources.MergedDictionaries
                .Where(d => d.Source != null && d.Source.OriginalString.IndexOf("Colors.", System.StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            // Only one Colors.* dictionary should remain
            Assert.AreEqual(1, colorDicts.Count, "There should be exactly one Colors.* dictionary after switching themes");
            Assert.IsTrue(colorDicts[0].Source.OriginalString.Contains("Colors.Dark.xaml"), "Remaining Colors dictionary should be the Dark theme");

            // Brushes.xaml should still be present
            bool brushesPresent = app.Resources.MergedDictionaries.Any(d => d.Source != null && d.Source.OriginalString.Contains("Brushes.xaml"));
            Assert.IsTrue(brushesPresent, "Brushes.xaml should remain in merged dictionaries");
        }

        [Test]
        public void PrimaryBrush_IsAvailable_InMergedResourcesAfterApplyTheme()
        {
            var app = _app;
            app.ApplyTheme("Dark");

            bool foundPrimaryBrush = false;
            foreach (var d in app.Resources.MergedDictionaries)
            {
                try
                {
                    if (d.Contains("PrimaryBrush"))
                    {
                        foundPrimaryBrush = true;
                        break;
                    }
                }
                catch
                {
                    // some dictionaries may throw if not loaded; ignore
                }
            }

            Assert.IsTrue(foundPrimaryBrush, "PrimaryBrush should be defined in merged resource dictionaries after applying theme");
        }
    }
}
