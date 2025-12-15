using NUnit.Framework;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System;
using System.Linq;
using System.Drawing;

namespace PhotoSorterApp.UITests
{
    [TestFixture]
    public class ThemeUiTests
    {
        private Application? _app;
        private AutomationBase? _automation;

        [SetUp]
        public void SetUp()
        {
            // Find solution root by locating PhotoSorterApp.csproj upwards from test assembly location
            var dir = new DirectoryInfo(AppContext.BaseDirectory!);
            DirectoryInfo? root = dir;
            while (root != null && !File.Exists(Path.Combine(root.FullName, "PhotoSorterApp", "PhotoSorterApp.csproj")))
            {
                root = root.Parent;
            }

            if (root == null)
                throw new DirectoryNotFoundException("Could not locate solution root containing PhotoSorterApp.csproj. Build the solution first.");

            var projectBin = Path.Combine(root.FullName, "PhotoSorterApp", "bin", "Debug");
            if (!Directory.Exists(projectBin))
                throw new DirectoryNotFoundException($"Build output folder not found: {projectBin}. Build the PhotoSorterApp project first.");

            // search for exe or dll under Debug folder (including platform subfolders)
            var exePath = Directory.GetFiles(projectBin, "PhotoSorterApp.exe", SearchOption.AllDirectories).FirstOrDefault();
            var dllPath = Directory.GetFiles(projectBin, "PhotoSorterApp.dll", SearchOption.AllDirectories).FirstOrDefault();

            ProcessStartInfo? psi = null;

            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                psi = new ProcessStartInfo(exePath) { UseShellExecute = false };
            }
            else if (!string.IsNullOrEmpty(dllPath) && File.Exists(dllPath))
            {
                psi = new ProcessStartInfo("dotnet", $"\"{dllPath}\"") { UseShellExecute = false };
            }

            if (psi == null)
                throw new FileNotFoundException("Application executable or DLL not found under bin\\Debug. Build the PhotoSorterApp project first.");

            _app = Application.Launch(psi);
            _automation = new UIA3Automation();
        }

        [TearDown]
        public void TearDown()
        {
            _automation?.Dispose();
            if (_app != null && !_app.HasExited)
            {
                _app.Close();
                _app.Dispose();
            }
        }

        private static Bitmap CaptureWindowBitmap(AutomationElement window)
        {
            var rect = window.BoundingRectangle;
            int x = (int)rect.X;
            int y = (int)rect.Y;
            int w = Math.Max(1, (int)rect.Width);
            int h = Math.Max(1, (int)rect.Height);

            var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
            return bmp;
        }

        private static Color GetAverageColor(Bitmap bmp, Rectangle? region = null)
        {
            var rect = region ?? new Rectangle(0, 0, bmp.Width, bmp.Height);
            long r = 0, g = 0, b = 0;
            int count = 0;
            // sample every Nth pixel for speed
            int stepX = Math.Max(1, rect.Width / 50);
            int stepY = Math.Max(1, rect.Height / 50);

            for (int x = rect.X; x < rect.X + rect.Width; x += stepX)
            {
                for (int y = rect.Y; y < rect.Y + rect.Height; y += stepY)
                {
                    var p = bmp.GetPixel(x, y);
                    r += p.R;
                    g += p.G;
                    b += p.B;
                    count++;
                }
            }

            if (count == 0) return Color.Empty;
            return Color.FromArgb((int)(r / count), (int)(g / count), (int)(b / count));
        }

        private static double ColorDistance(Color a, Color b)
        {
            // Euclidean distance in RGB space
            var dr = a.R - b.R;
            var dg = a.G - b.G;
            var db = a.B - b.B;
            return Math.Sqrt(dr * dr + dg * dg + db * db);
        }

        [Test]
        public void ThemeMenu_SwitchesTheme_ScreenshotComparison()
        {
            var window = _app!.GetMainWindow(_automation!);
            Assert.IsNotNull(window, "Main window should be available");

            // ensure window has focus
            window.Focus();
            Thread.Sleep(300);

            // capture before
            using var beforeBmp = CaptureWindowBitmap(window);

            // find theme menu and click dark then light like previous test
            // search for Theme top-level menu item by name
            MenuItem? themeMenuItem = null;
            var topLevelMenuItems = window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.MenuItem));
            foreach (var mi in topLevelMenuItems)
            {
                var name = mi.Properties.Name.ValueOrDefault ?? string.Empty;
                if (string.Equals(name.Trim(), "Тема", StringComparison.OrdinalIgnoreCase) || string.Equals(name.Trim(), "Theme", StringComparison.OrdinalIgnoreCase))
                {
                    themeMenuItem = mi.AsMenuItem();
                    break;
                }
            }

            Assert.IsNotNull(themeMenuItem, "Theme menu item should be found");

            // open the Theme menu
            themeMenuItem.Click();
            Thread.Sleep(250);

            // find dark menu item
            MenuItem? darkItem = null;
            var allMenuItemsNow = window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.MenuItem));
            foreach (var mi in allMenuItemsNow)
            {
                var name = (mi.Properties.Name.ValueOrDefault ?? string.Empty).Trim();
                if (name.Equals("Тёмная", StringComparison.OrdinalIgnoreCase) || name.Equals("Темная", StringComparison.OrdinalIgnoreCase) || name.Equals("Dark", StringComparison.OrdinalIgnoreCase))
                {
                    darkItem = mi.AsMenuItem();
                    break;
                }
            }

            Assert.IsNotNull(darkItem, "Dark submenu item should be found in the open menu");

            // click dark
            darkItem!.Click();
            Thread.Sleep(600);

            // capture after dark
            using var darkBmp = CaptureWindowBitmap(window);

            // reopen and click light
            themeMenuItem.Click();
            Thread.Sleep(200);

            MenuItem? lightItem = null;
            var allMenuItemsAfter = window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.MenuItem));
            foreach (var mi in allMenuItemsAfter)
            {
                var name = (mi.Properties.Name.ValueOrDefault ?? string.Empty).Trim();
                if (name.Equals("Светлая", StringComparison.OrdinalIgnoreCase) || name.Equals("Light", StringComparison.OrdinalIgnoreCase))
                {
                    lightItem = mi.AsMenuItem();
                    break;
                }
            }

            Assert.IsNotNull(lightItem, "Light submenu item should be found after reopening the menu");

            lightItem!.Click();
            Thread.Sleep(600);

            // capture after light
            using var lightBmp = CaptureWindowBitmap(window);

            // compute average colors of center region for before and after
            var centerRect = new Rectangle(beforeBmp.Width / 4, beforeBmp.Height / 4, beforeBmp.Width / 2, beforeBmp.Height / 2);
            var beforeColor = GetAverageColor(beforeBmp, centerRect);
            var darkColor = GetAverageColor(darkBmp, centerRect);
            var lightColor = GetAverageColor(lightBmp, centerRect);

            // Save artifacts for manual inspection
            var outDir = Path.Combine(Path.GetTempPath(), "PhotoSorterApp_UITest_Screenshots");
            Directory.CreateDirectory(outDir);
            var beforePath = Path.Combine(outDir, "before.png");
            var darkPath = Path.Combine(outDir, "dark.png");
            var lightPath = Path.Combine(outDir, "light.png");
            beforeBmp.Save(beforePath, System.Drawing.Imaging.ImageFormat.Png);
            darkBmp.Save(darkPath, System.Drawing.Imaging.ImageFormat.Png);
            lightBmp.Save(lightPath, System.Drawing.Imaging.ImageFormat.Png);

            // Compare before->dark and dark->light distances
            var distBeforeDark = ColorDistance(beforeColor, darkColor);
            var distDarkLight = ColorDistance(darkColor, lightColor);

            // Log distances
            TestContext.WriteLine($"AvgColor Before: {beforeColor}, Dark: {darkColor}, Light: {lightColor}");
            TestContext.WriteLine($"Distance Before->Dark: {distBeforeDark:F2}, Dark->Light: {distDarkLight:F2}");
            TestContext.WriteLine($"Screenshots saved to: {outDir}");

            // require that dark differs from light by a noticeable amount
            const double threshold = 15.0; // tuned threshold
            if (distDarkLight < threshold)
            {
                Assert.Fail($"Theme visual did not change significantly. Dark->Light distance: {distDarkLight:F2} < threshold {threshold}. Check screenshots at {outDir}");
            }

            Assert.Pass("Theme visual changed (screenshot comparison). Artifacts saved at: " + outDir);
        }
    }
}
