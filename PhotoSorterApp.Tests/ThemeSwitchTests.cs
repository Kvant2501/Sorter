using NUnit.Framework;
using System.Windows;
using PhotoSorterApp;
using System.Linq;

namespace PhotoSorterApp.Tests
{
    [TestFixture, Apartment(System.Threading.ApartmentState.STA)]
    public class ThemeSwitchTests
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
                // create Application only if none exists
                _app = new App();
            }
        }

        [Test]
        public void ApplyTheme_SwitchesCurrentThemeAndResourceDictionary()
        {
            var app = _app;

            // ensure starting state
            app.ApplyTheme("Light");
            Assert.AreEqual("Light", app.CurrentTheme);

            app.ApplyTheme("Dark");
            Assert.AreEqual("Dark", app.CurrentTheme);

            // Check that a Colors.Dark resource dictionary is present
            bool found = false;
            foreach (var dict in app.Resources.MergedDictionaries)
            {
                if (dict.Source != null && dict.Source.OriginalString.Contains("Colors.Dark.xaml"))
                {
                    found = true;
                    break;
                }
            }

            Assert.IsTrue(found, "Colors.Dark.xaml should be present in MergedDictionaries after ApplyTheme(\"Dark\")");

            app.ApplyTheme("Light");
            Assert.AreEqual("Light", app.CurrentTheme);

            found = false;
            foreach (var dict in app.Resources.MergedDictionaries)
            {
                if (dict.Source != null && dict.Source.OriginalString.Contains("Colors.Light.xaml"))
                {
                    found = true;
                    break;
                }
            }

            Assert.IsTrue(found, "Colors.Light.xaml should be present in MergedDictionaries after ApplyTheme(\"Light\")");
        }
    }
}