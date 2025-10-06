using System;
using System.Globalization;
using System.Windows.Data;

namespace Smotrel.Converters
{
    public class SpeedHighlightConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2
                && values[0] is double current
                && values[1] is double buttonSpeed)
            {
                return Math.Abs(current - buttonSpeed) < 0.0001;
            }
            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
