using System.Globalization;

namespace RichEdit.Maui.TestApp;

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is false;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is false;
}
