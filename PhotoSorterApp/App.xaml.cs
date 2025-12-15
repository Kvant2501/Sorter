using System.Linq;
using System.Windows;
using System.IO;
using System.Windows.Media;

namespace PhotoSorterApp;

public partial class App : Application
{
    private static readonly string ThemeFolder = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "PhotoSorter");
    private static readonly string ThemeFile = Path.Combine(ThemeFolder, "theme.txt");

    public string CurrentTheme { get; private set; } = "Light";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var theme = "Light";
            if (File.Exists(ThemeFile))
            {
                var t = File.ReadAllText(ThemeFile).Trim();
                if (!string.IsNullOrEmpty(t)) theme = t;
            }

            ApplyTheme(theme);
        }
        catch
        {
            // ignore errors, fallback to default resources
        }
    }

    public void ApplyTheme(string themeName)
    {
        // themeName: "Light" or "Dark"
        var dictionaries = Resources.MergedDictionaries;
        // Remove any Colors.* dictionary (robust, case-insensitive)
        var existingColors = dictionaries.Where(d => d.Source != null && d.Source.OriginalString?.IndexOf("Colors.", System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        foreach (var d in existingColors)
            dictionaries.Remove(d);

        // Use pack URI to reliably locate resource in the assembly
        var newColorsUri = new System.Uri($"pack://application:,,,/PhotoSorterApp;component/Themes/Colors.{themeName}.xaml", System.UriKind.Absolute);

        // Insert new Colors dictionary at position 0 so Brushes.xaml can resolve correctly
        dictionaries.Insert(0, new ResourceDictionary { Source = newColorsUri });

        // Recreate Brushes.xaml so brushes are recreated and can pick up new DynamicResource values
        var brushesUri = new System.Uri("pack://application:,,,/PhotoSorterApp;component/Themes/Brushes.xaml", System.UriKind.Absolute);
        var existingBrushes = dictionaries.Where(d => d.Source != null && d.Source.OriginalString?.IndexOf("Brushes.xaml", System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        foreach (var b in existingBrushes)
            dictionaries.Remove(b);

        dictionaries.Insert(1, new ResourceDictionary { Source = brushesUri });

        CurrentTheme = themeName;

        // persist selection
        try
        {
            if (!Directory.Exists(ThemeFolder)) Directory.CreateDirectory(ThemeFolder);
            File.WriteAllText(ThemeFile, themeName);
        }
        catch
        {
            // ignore write errors
        }

        // Diagnostic log for UI tests: write merged dictionary sources to temp file
        try
        {
            var tmp = Path.Combine(Path.GetTempPath(), "PhotoSorterApp_ThemeLog.txt");
            using var sw = new StreamWriter(tmp, append: true);
            sw.WriteLine($"[{System.DateTime.Now:O}] ApplyTheme called: {themeName}");
            foreach (var d in dictionaries)
            {
                sw.WriteLine(d.Source?.OriginalString ?? "(inline)");
            }
            sw.WriteLine("---");
        }
        catch { }

        // Force re-apply resource references on all open windows to update elements that didn't pick up DynamicResource
        try
        {
            RefreshWindowsResources();
        }
        catch { }
    }

    private void RefreshWindowsResources()
    {
        var brushKeys = new[] { "BackgroundBrush", "SurfaceBrush", "PrimaryBrush", "BorderBrush", "TextBrush", "SubtleTextBrush", "TextOnPrimaryBrush" };

        foreach (Window win in Current.Windows)
        {
            // ensure we run on UI thread
            win.Dispatcher.Invoke(() =>
            {
                // Set resource references on the window itself
                win.SetResourceReference(Window.BackgroundProperty, "BackgroundBrush");
                // traverse logical tree and set common properties to resource references
                TryApplyResourceRefs(win);
                // force layout update
                win.InvalidateVisual();
                win.UpdateLayout();
            });
        }

        void TryApplyResourceRefs(DependencyObject node)
        {
            if (node is FrameworkElement fe)
            {
                // set common properties to resource references so they update when resources change
                if (fe.TryFindResource("SurfaceBrush") != null)
                    fe.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
                if (fe.TryFindResource("PrimaryBrush") != null)
                    fe.SetResourceReference(Control.BorderBrushProperty, "PrimaryBrush");
                if (fe.TryFindResource("TextBrush") != null)
                    fe.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

                // special-case Buttons
                if (fe is Control)
                {
                    if (fe.TryFindResource("PrimaryBrush") != null)
                        fe.SetResourceReference(Control.BackgroundProperty, "PrimaryBrush");
                }
            }

            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            {
                TryApplyResourceRefs(child);
            }
        }
    }
}