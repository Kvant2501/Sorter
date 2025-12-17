using System.Linq;
using System.Windows;
using System.IO;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Controls;

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

        // Ensure top-level brushes are recreated from color tokens so UI updates reliably
        try
        {
            // Map of color keys -> brush keys
            var map = new (string colorKey, string brushKey)[]
            {
                ("PrimaryColor", "PrimaryBrush"),
                ("AccentColor", "AccentBrush"),
                ("BackgroundColor", "BackgroundBrush"),
                ("SurfaceColor", "SurfaceBrush"),
                ("BorderColor", "BorderBrush"),
                ("TextColor", "TextBrush"),
                ("SubtleTextColor", "SubtleTextBrush"),
                ("TextOnPrimaryColor", "TextOnPrimaryBrush"),
                ("ShadowColor", "ShadowColor")
            };

            foreach (var (colorKey, brushKey) in map)
            {
                if (Resources.Contains(colorKey))
                {
                    var colorObj = Resources[colorKey];
                    if (colorObj is Color c)
                    {
                        if (brushKey == "ShadowColor")
                        {
                            Resources[brushKey] = c;
                        }
                        else
                        {
                            var brush = new SolidColorBrush(c);
                            Resources[brushKey] = brush;
                        }
                    }
                }
            }
        }
        catch
        {
            // best-effort: do not break theme change on errors
        }

        // Safer visual refresh: reset and restore Style on framework elements to force DynamicResource re-evaluation
        try
        {
            foreach (Window w in Current.Windows)
            {
                // Update window background and foreground from theme resources
                try
                {
                    w.Dispatcher.Invoke(() =>
                    {
                        if (Resources.Contains("BackgroundBrush") && Resources["BackgroundBrush"] is Brush bg)
                        {
                            w.Background = bg;
                        }

                        if (Resources.Contains("TextBrush") && Resources["TextBrush"] is Brush fg)
                        {
                            w.Foreground = fg;
                        }
                    }, System.Windows.Threading.DispatcherPriority.Render);
                }
                catch { }

                w.Dispatcher.Invoke(() =>
                {
                    void Refresh(DependencyObject obj)
                    {
                        if (obj is FrameworkElement fe && !(fe is Window))
                        {
                            var savedStyle = fe.Style;
                            fe.Style = null;
                            fe.Style = savedStyle;
                        }

                        if (obj is FrameworkElement fe2)
                        {
                            fe2.InvalidateVisual();
                            try { fe2.UpdateLayout(); } catch { }
                        }

                        int children = VisualTreeHelper.GetChildrenCount(obj);
                        for (int i = 0; i < children; i++)
                        {
                            var child = VisualTreeHelper.GetChild(obj, i);
                            Refresh(child);
                        }
                    }

                    Refresh(w);
                }, System.Windows.Threading.DispatcherPriority.Render);
            }
        }
        catch
        {
            // best-effort; do not abort theme change on refresh errors
        }

        // Export some values and map system brushes so popups and system templates follow the theme
        try
        {
            try
            {
                if (Resources.Contains("SurfaceBrush")) Resources[SystemColors.MenuBrushKey] = Resources["SurfaceBrush"];
                if (Resources.Contains("TextBrush")) Resources[SystemColors.MenuTextBrushKey] = Resources["TextBrush"];
                if (Resources.Contains("PrimaryBrush")) Resources[SystemColors.HighlightBrushKey] = Resources["PrimaryBrush"];
                if (Resources.Contains("TextOnPrimaryBrush")) Resources[SystemColors.HighlightTextBrushKey] = Resources["TextOnPrimaryBrush"];
                if (Resources.Contains("SurfaceBrush")) Resources[SystemColors.ControlBrushKey] = Resources["SurfaceBrush"];
                if (Resources.Contains("TextBrush")) Resources[SystemColors.ControlTextBrushKey] = Resources["TextBrush"];
            }
            catch { }

            // For UI tests: write FG/BG for key elements into AutomationProperties.HelpText
            foreach (Window w in Current.Windows)
            {
                try
                {
                    w.Dispatcher.Invoke(() =>
                    {
                        FrameworkElement? label = null;
                        FrameworkElement? btn = null;
                        try { label = w.FindName("LabelFileType") as FrameworkElement; } catch { }
                        try { btn = w.FindName("StartButton") as FrameworkElement; } catch { }

                        if (label != null)
                        {
                            SolidColorBrush? fgBrush = null;
                            SolidColorBrush? bgBrush = null;

                            if (label is TextBlock tb)
                            {
                                fgBrush = tb.Foreground as SolidColorBrush;
                                bgBrush = tb.Background as SolidColorBrush;
                            }

                            fgBrush ??= Resources["TextBrush"] as SolidColorBrush;
                            bgBrush ??= Resources["BackgroundBrush"] as SolidColorBrush;

                            var text = $"FG={ColorToHex(fgBrush?.Color)};BG={ColorToHex(bgBrush?.Color)}";
                            AutomationProperties.SetHelpText(label, text);
                        }

                        if (btn != null)
                        {
                            SolidColorBrush? fgBrush = null;
                            SolidColorBrush? bgBrush = null;

                            if (btn is Control ctrl)
                            {
                                fgBrush = ctrl.Foreground as SolidColorBrush;
                                bgBrush = ctrl.Background as SolidColorBrush;
                            }

                            fgBrush ??= Resources["TextOnPrimaryBrush"] as SolidColorBrush;
                            bgBrush ??= Resources["PrimaryBrush"] as SolidColorBrush;

                            var text = $"FG={ColorToHex(fgBrush?.Color)};BG={ColorToHex(bgBrush?.Color)}";
                            AutomationProperties.SetHelpText(btn, text);
                        }
                    });
                }
                catch { }
            }
        }
        catch { }

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

            // Also log resolved colors/brushes for debugging
            string[] brushKeys = new[] { "PrimaryBrush", "AccentBrush", "BackgroundBrush", "SurfaceBrush", "BorderBrush", "TextBrush", "SubtleTextBrush", "TextOnPrimaryBrush" };
            foreach (var key in brushKeys)
            {
                if (Resources.Contains(key))
                {
                    var val = Resources[key];
                    if (val is SolidColorBrush sb)
                    {
                        sw.WriteLine($"Resource {key}: SolidColorBrush Color={sb.Color}");
                    }
                    else if (val is Brush b)
                    {
                        sw.WriteLine($"Resource {key}: Brush Type={b.GetType().FullName}");
                    }
                    else
                    {
                        sw.WriteLine($"Resource {key}: {val?.GetType().FullName ?? "null"} -> {val}");
                    }
                }
                else
                {
                    sw.WriteLine($"Resource {key}: (not found)");
                }
            }

            // Also log top-level color tokens if present
            string[] colorKeys = new[] { "PrimaryColor", "BackgroundColor", "SurfaceColor", "TextColor", "SubtleTextColor", "BorderColor", "TextOnPrimaryColor", "ShadowColor" };
            foreach (var key in colorKeys)
            {
                if (Resources.Contains(key))
                {
                    var val = Resources[key];
                    sw.WriteLine($"Color token {key}: {val?.ToString() ?? "null"}");
                }
                else
                {
                    sw.WriteLine($"Color token {key}: (not found)");
                }
            }

            sw.WriteLine("---");
        }
        catch { }
    }

    private static string ColorToHex(Color? c)
    {
        if (c == null) return "#NULL";
        return c.Value.ToString();
    }
}