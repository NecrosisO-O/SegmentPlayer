using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PortablePlayer.UI.Converters;

public sealed class BoolToGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isOpen = value is true;
        return isOpen ? new GridLength(360) : new GridLength(0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
