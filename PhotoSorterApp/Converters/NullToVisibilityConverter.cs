#nullable enable
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PhotoSorterApp.Converters;

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var isVisible = value != null;
        if (parameter is string param && param == "Collapsed")
            isVisible = !isVisible;

        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}