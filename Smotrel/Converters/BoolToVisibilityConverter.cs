using System.Globalization;
using System.Windows;
using System.Windows.Data;


namespace Smotrel.Converters
{
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }
        public Visibility FalseVisibility { get; set; } = Visibility.Collapsed;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (targetType != typeof(Visibility))
                throw new InvalidOperationException("Target type must be Visibility.");

            bool flag = false;

            if (value is bool b)
                flag = b;
            else if (value is bool?)
                flag = (bool)value;

            if (Invert)
                flag = !flag;

            return flag ? Visibility.Visible : FalseVisibility;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not Visibility visibility)
                return DependencyProperty.UnsetValue;

            bool result = visibility == Visibility.Visible;

            if (Invert)
                result = !result;

            if (targetType == typeof(bool) || targetType == typeof(bool?))
                return result;

            return DependencyProperty.UnsetValue;
        }
    }
}
