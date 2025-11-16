#nullable enable

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PhotoSorterApp.Converters;

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isNull = value == null;
        if (parameter?.ToString().Equals("Inverse", StringComparison.OrdinalIgnoreCase) == true)
            isNull = !isNull;
        return isNull ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}