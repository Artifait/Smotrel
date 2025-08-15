using System;
using System.Globalization;
using System.Windows.Data;

namespace Smotrel.Converters
{
    /// <summary>
    /// Преобразует булево значение (Watched) в строку:
    /// по умолчанию: true -> "Просмотрено", false -> "" (пусто).
    /// Если передать параметр "text" через ConverterParameter в формате "YesText|NoText",
    /// то он будет использоваться (например "✓|").
    /// </summary>
    public class WatchedToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var yes = "Просмотрено";
            var no = string.Empty;

            // поддержка параметра "Yes|No"
            if (parameter is string p && p.Contains("|"))
            {
                var parts = p.Split(new[] { '|' }, 2);
                if (parts.Length == 2)
                {
                    yes = parts[0];
                    no = parts[1];
                }
            }

            try
            {
                bool isWatched = false;
                if (value is bool b) isWatched = b;
                else
                {
                    var s = value?.ToString();
                    if (!string.IsNullOrEmpty(s) && bool.TryParse(s, out var parsed))
                        isWatched = parsed;
                }

                return isWatched ? yes : no;
            }
            catch
            {
                return no;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
