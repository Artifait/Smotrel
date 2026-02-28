using System.Runtime.InteropServices;

namespace Smotrel.Services
{
    /// <summary>
    /// Натуральная сортировка строк — числа сравниваются как числа, не лексикографически.
    ///
    /// Примеры:
    ///   Лексикографически: "1", "10", "2", "20", "3"
    ///   Натурально:        "1", "2",  "3", "10", "20"
    ///
    ///   "[Tag] 2.1 Видео"  →  перед  →  "[Tag] 2.10 Видео"
    ///
    /// Использует Windows Shell StrCmpLogicalW когда доступно (Windows),
    /// иначе падает на ручной разбор числовых блоков.
    /// </summary>
    public sealed class NaturalSortComparer : IComparer<string>
    {
        public static readonly NaturalSortComparer Instance = new();

        private NaturalSortComparer() { }

        public int Compare(string? x, string? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            // Пробуем системный StrCmpLogicalW (доступен на Windows)
            try
            {
                return StrCmpLogicalW(x, y);
            }
            catch
            {
                return FallbackCompare(x, y);
            }
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string x, string y);

        /// <summary>
        /// Резервная реализация натуральной сортировки без P/Invoke.
        /// Разбивает строки на блоки цифр и нецифр и сравнивает их.
        /// </summary>
        private static int FallbackCompare(string x, string y)
        {
            int ix = 0, iy = 0;

            while (ix < x.Length && iy < y.Length)
            {
                bool xIsDigit = char.IsDigit(x[ix]);
                bool yIsDigit = char.IsDigit(y[iy]);

                if (xIsDigit && yIsDigit)
                {
                    // Числовой блок: сравниваем как int
                    int numX = 0, numY = 0;
                    while (ix < x.Length && char.IsDigit(x[ix])) numX = numX * 10 + (x[ix++] - '0');
                    while (iy < y.Length && char.IsDigit(y[iy])) numY = numY * 10 + (y[iy++] - '0');

                    int cmp = numX.CompareTo(numY);
                    if (cmp != 0) return cmp;
                }
                else
                {
                    // Текстовый блок: сравниваем символ за символом
                    int cmp = char.ToUpperInvariant(x[ix])
                        .CompareTo(char.ToUpperInvariant(y[iy]));
                    if (cmp != 0) return cmp;
                    ix++;
                    iy++;
                }
            }

            return x.Length.CompareTo(y.Length);
        }
    }
}
