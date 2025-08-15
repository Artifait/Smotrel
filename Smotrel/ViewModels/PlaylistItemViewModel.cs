using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace Smotrel.ViewModels
{
    public class PlaylistItemViewModel : INotifyPropertyChanged
    {
        // Исходные данные
        public string FilePath { get; init; } = string.Empty;
        public string PartId { get; init; } = string.Empty;
        public string ChapterId { get; init; } = string.Empty;
        public string CourseId { get; init; } = string.Empty;

        // Полное "чистое" название без платформы (пример: "09. Установка Docker Desktop")
        public string FullTitle { get; init; }

        // Укороченная версия для отображения (с аккуратной усечённой по словам логикой)
        public string ShortTitle { get; init; }

        public long? Duration { get; init; }
        public long LastPosition { get; init; }

        private bool _watched;
        public bool Watched
        {
            get => _watched;
            set
            {
                if (_watched == value) return;
                _watched = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Watched)));
            }
        }

        public PlaylistItemViewModel(string rawTitle,
                                     string filePath,
                                     string partId = null,
                                     string chapterId = null,
                                     string courseId = null,
                                     long? duration = null,
                                     long lastPosition = 0,
                                     bool watched = false,
                                     int shortTitleMaxWords = 5,
                                     int shortTitleMaxChars = 36)
        {
            FilePath = filePath ?? string.Empty;
            PartId = partId ?? string.Empty;
            ChapterId = chapterId ?? string.Empty;
            CourseId = courseId ?? string.Empty;

            // Сначала очищаем платформу [..] в начале
            FullTitle = StripPlatformPrefix(rawTitle ?? PathToTitle(filePath));

            // затем создаём укороченную версию
            ShortTitle = MakeShortTitle(FullTitle, shortTitleMaxWords, shortTitleMaxChars);

            Duration = duration;
            LastPosition = lastPosition;
            Watched = watched;
        }

        // Уберите начало в скобках: [something] ...
        private static string StripPlatformPrefix(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            // удаляем один ведущий блок [..] плюс пробелы
            var s = Regex.Replace(input, @"^\s*\[[^\]]*\]\s*", string.Empty);
            return s.Trim();
        }

        // fallback: если нет названия — взять имя файла
        private static string PathToTitle(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return string.Empty;
                return System.IO.Path.GetFileNameWithoutExtension(path);
            }
            catch
            {
                return string.Empty;
            }
        }

        // Укорачивает аккуратно по словам, если слишком длинно — добавляет "…"
        private static string MakeShortTitle(string full, int maxWords, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(full)) return full ?? string.Empty;

            // Если в пределах лимита — вернуть как есть
            if (full.Length <= maxChars && full.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= maxWords)
                return full;

            // Разделим по пробелам и набираем слова, пока не превысим лимит
            var words = full.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var cur = "";
            for (int i = 0; i < words.Length; i++)
            {
                var next = (cur.Length == 0) ? words[i] : cur + " " + words[i];
                if (next.Length > maxChars || i + 1 > maxWords)
                {
                    // если нет ни одного слова — всё равно обрежем символы
                    if (string.IsNullOrEmpty(cur))
                    {
                        var trimmed = full.Length > maxChars ? full.Substring(0, Math.Max(0, maxChars - 1)).TrimEnd() + "…" : full;
                        return trimmed;
                    }
                    return cur.TrimEnd() + "…";
                }
                cur = next;
            }

            // все слова поместились — но общая длина могла быть больше; в любом случае вернуть cur
            return cur;
        }

        // INotify
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
