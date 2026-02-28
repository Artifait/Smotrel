using System.Text.RegularExpressions;

namespace Smotrel.Services
{
    /// <summary>
    /// Удаляет общий повторяющийся префикс из названий глав и видео.
    ///
    /// Пример:
    ///   "[SuperSliv.biz] Старт в DevOps"
    ///   "[SuperSliv.biz] 2. Linux первые шаги"
    ///   "[SuperSliv.biz] 2.1 Разбор домашнего задания"
    ///   → общий префикс "[SuperSliv.biz]" удаляется из ВСЕХ имён.
    ///
    /// Алгоритм:
    ///   1. Ищем паттерн-кандидат по первому имени.
    ///   2. Проверяем, что кандидат присутствует у строгого большинства имён.
    ///   3. Если да — удаляем у всех через тот же паттерн (не строковое сравнение).
    ///   4. Повторяем до стабилизации (на случай "[A] [B] имя").
    ///
    /// Отличие от предыдущей версии: очистка делается через повторный матч паттерна,
    /// а не через сравнение строк — поэтому разное количество пробелов не ломает результат.
    /// </summary>
    public static class LabelCleaner
    {
        // Паттерны-кандидаты в порядке приоритета
        private static readonly Regex[] _candidates =
        [
            // [тег] — самый частый: [SuperSliv.biz], [HD] и т.д.
            new(@"^\[[^\]]+\]\s*", RegexOptions.Compiled),
            // (тег) — скобочные теги
            new(@"^\([^\)]+\)\s*", RegexOptions.Compiled),
            // domain.tld без скобок: "SuperSliv.biz "
            new(@"^[A-Za-z0-9][A-Za-z0-9\-]*\.[A-Za-z]{2,}\s+", RegexOptions.Compiled),
        ];

        // Минимальная доля имён (%), у которых должен совпасть префикс
        private const double MinCoverageRatio = 0.85;

        // ── Публичный API ─────────────────────────────────────────────────────

        /// <summary>
        /// Строит словарь rawName → cleanedLabel для набора имён.
        /// </summary>
        public static Dictionary<string, string> BuildCleanMap(IEnumerable<string> rawNames)
        {
            var names = rawNames.Distinct().ToList();
            if (names.Count == 0) return [];

            // Находим все общие паттерны-префиксы
            var commonPatterns = FindCommonPatterns(names);

            // Строим словарь: raw → cleaned
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in names)
                map[name] = ApplyPatterns(name, commonPatterns);

            return map;
        }

        // ── Поиск общих паттернов ─────────────────────────────────────────────

        /// <summary>
        /// Возвращает список паттернов, которые являются общим префиксом
        /// для большинства имён.
        /// </summary>
        private static List<Regex> FindCommonPatterns(List<string> names)
        {
            var result = new List<Regex>();

            // Работаем итерационно: один проход снимает один уровень тегов
            var working = names.ToList();

            for (int iteration = 0; iteration < 5; iteration++)
            {
                bool foundAny = false;

                foreach (var pattern in _candidates)
                {
                    // Нормализуем совпадение: берём только внутренность тега (без пробелов)
                    var matchedCore = ExtractCores(working, pattern);
                    if (matchedCore == null) continue;

                    // Проверяем долю покрытия
                    int matchCount = working.Count(n =>
                    {
                        var m = pattern.Match(n);
                        return m.Success && NormalizeTag(m.Value) == matchedCore;
                    });

                    double ratio = (double)matchCount / working.Count;
                    if (ratio < MinCoverageRatio) continue;

                    // Паттерн общий — добавляем и применяем к рабочему списку
                    result.Add(pattern);
                    working = working.Select(n => ApplyPatterns(n, [pattern])).ToList();
                    foundAny = true;
                    break; // один паттерн за итерацию
                }

                if (!foundAny) break;
            }

            return result;
        }

        /// <summary>
        /// Извлекает нормализованное "ядро" тега из первого совпавшего имени.
        /// Возвращает null если первое имя не матчится.
        /// </summary>
        private static string? ExtractCores(List<string> names, Regex pattern)
        {
            // Перебираем первые несколько имён — берём самый часто встречающийся тег
            var cores = names
                .Take(Math.Min(names.Count, 10))
                .Select(n => { var m = pattern.Match(n); return m.Success ? NormalizeTag(m.Value) : null; })
                .Where(c => c != null)
                .GroupBy(c => c)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            return cores?.Key;
        }

        /// <summary>
        /// Нормализует совпавший тег: убирает пробелы, приводит к нижнему регистру.
        /// "[SuperSliv.biz] " → "[suprersliv.biz]"
        /// Нужно чтобы "[A] " и "[A]  " считались одинаковыми.
        /// </summary>
        private static string NormalizeTag(string matched)
            => matched.Trim().ToLowerInvariant();

        // ── Применение паттернов ──────────────────────────────────────────────

        /// <summary>
        /// Удаляет все совпадающие префиксы из строки через повторное применение паттернов.
        /// </summary>
        private static string ApplyPatterns(string name, List<Regex> patterns)
        {
            string result = name;

            bool changed;
            do
            {
                changed = false;
                foreach (var p in patterns)
                {
                    var m = p.Match(result);
                    if (m.Success && m.Index == 0)
                    {
                        result = result[m.Length..];
                        changed = true;
                    }
                }
            } while (changed);

            return result.Trim();
        }
    }
}