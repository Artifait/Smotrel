using System;
using System.Globalization;
using System.Windows.Data;

namespace Smotrel.Converters
{
    /// <summary>
    /// Преобразует количество элементов в высоту ListBox:
    /// Height = Min(itemCount * itemHeight + padding, maxHeight)
    /// ConverterParameter: "<itemHeight>|<maxHeight>|<padding>" (в пикселях). Пример: "56|420|16"
    /// Если не задан параметр — берёт itemHeight=56, maxHeight=420, padding=16
    /// </summary>
    public class ItemsCountToHeightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int count = 0;
            if (value is int i) count = i;
            else if (value is long l) count = (int)l;
            else if (!int.TryParse(value?.ToString() ?? "0", out count)) count = 0;

            double itemHeight = 56;
            double maxHeight = 420;
            double padding = 16;

            if (parameter is string p)
            {
                var parts = p.Split('|');
                if (parts.Length > 0 && double.TryParse(parts[0], out var ih)) itemHeight = ih;
                if (parts.Length > 1 && double.TryParse(parts[1], out var mh)) maxHeight = mh;
                if (parts.Length > 2 && double.TryParse(parts[2], out var pd)) padding = pd;
            }

            var desired = Math.Max(32, count * itemHeight + padding);
            return Math.Min(desired, maxHeight);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
