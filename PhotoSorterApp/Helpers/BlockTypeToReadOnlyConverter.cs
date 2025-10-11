using PhotoSorterApp.Models;
using System;
using System.Globalization;
using System.Windows.Data;

namespace PhotoSorterApp.Helpers;

public class BlockTypeToReadOnlyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is RenameTemplateBlock.BlockType type)
        {
            return type != RenameTemplateBlock.BlockType.Text;
        }
        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}