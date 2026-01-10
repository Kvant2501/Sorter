using NUnit.Framework;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PhotoSorterApp.Tests;

[TestFixture, Apartment(ApartmentState.STA)]
public class ButtonTemplateForegroundTests
{
    [Test]
    public void PrimaryButton_TemplateText_ShouldUse_TextOnPrimaryBrush()
    {
        var app = Application.Current!;

        var expected = app.Resources["TextOnPrimaryBrush"] as SolidColorBrush;
        Assert.IsNotNull(expected);

        var btn = new Button
        {
            Style = (Style)app.Resources["PrimaryButton"],
            Content = "Старт",
            Width = 120,
            Height = 40
        };

        var host = new Window
        {
            Content = btn,
            Width = 200,
            Height = 120,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false
        };

        try
        {
            host.Show();
            host.UpdateLayout();

            var tb = FindDescendant<TextBlock>(btn);
            Assert.IsNotNull(tb, "Button template should contain a TextBlock");

            var tbFg = tb!.Foreground as SolidColorBrush;
            Assert.IsNotNull(tbFg, "TextBlock.Foreground should be SolidColorBrush");

            Assert.AreEqual(expected!.Color, tbFg!.Color);
        }
        finally
        {
            host.Close();
        }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) return t;
            var found = FindDescendant<T>(child);
            if (found != null) return found;
        }
        return null;
    }
}
