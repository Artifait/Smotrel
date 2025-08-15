using System;
using System.Globalization;
using System.Windows.Data;

namespace Smotrel.Converters
{
    /// <summary>
    /// Конвертирует длительность в секундах (long/int/double/TimeSpan/строка) в читаемую строку "H:MM:SS" или "M:SS".
    /// Возвращает "0:00" для нуля/неизвестно.
    /// </summary>
    public class DurationToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                long seconds = 0;

                if (value == null) return "0:00";

                if (value is long l) seconds = l;
                else if (value is int i) seconds = i;
                else if (value is double d) seconds = (long)Math.Round(d);
                else if (value is float f) seconds = (long)Math.Round(f);
                else if (value is TimeSpan ts) seconds = (long)Math.Round(ts.TotalSeconds);
                else
                {
                    // попытка парсинга строки
                    var s = value.ToString();
                    if (!long.TryParse(s, out seconds))
                    {
                        if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var dv))
                            seconds = (long)Math.Round(dv);
                        else
                            return "0:00";
                    }
                }

                if (seconds <= 0) return "0:00";

                var t = TimeSpan.FromSeconds(seconds);
                if (t.TotalHours >= 1)
                    return t.ToString(@"h\:mm\:ss");
                return t.ToString(@"m\:ss");
            }
            catch
            {
                return "0:00";
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // обратное преобразование не требуется
            throw new NotImplementedException();
        }
    }
}
