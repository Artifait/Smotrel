
using System.Globalization;
using System.Windows.Data;

namespace Smotrel.Converters
{
    public sealed class MultiplyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is double v && double.TryParse(parameter.ToString(), out var k) ? v * k : System.Windows.Data.Binding.DoNothing;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => System.Windows.Data.Binding.DoNothing;
    }
}
