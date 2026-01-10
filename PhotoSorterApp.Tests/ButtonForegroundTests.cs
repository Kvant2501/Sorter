using NUnit.Framework;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace PhotoSorterApp.Tests;

[TestFixture, Apartment(ApartmentState.STA)]
public class ButtonForegroundTests
{
    [Test]
    public void PrimaryButton_Style_ShouldBeResolvable()
    {
        var app = Application.Current!;

        Assert.That(app.Resources.Contains("PrimaryButton"), Is.True);
        Assert.That(app.Resources.Contains("TextOnPrimaryBrush"), Is.True);

        var btn = new Button { Style = (Style)app.Resources["PrimaryButton"], Content = "Старт" };
        btn.ApplyTemplate();
    }
}
