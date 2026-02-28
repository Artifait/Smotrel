using Smotrel.Interfaces;
using Smotrel.Models;
using System.ComponentModel.DataAnnotations.Schema;

namespace Smotrel.Models
{
    /// <summary>
    /// Модель видеоурока.
    ///
    /// Поля хранения (EF):
    ///   Path        — относительный путь или имя файла (в БД)
    ///   Duration    — длительность (кешируется в БД после первого открытия)
    ///
    /// Поля только для runtime (NotMapped):
    ///   FilePath    — ПОЛНЫЙ абсолютный путь, нужен SmotrelPlayer.
    ///                 Заполняется CourseBuilderService при сканировании папки.
    ///   Timestamps  — таймкоды главы
    /// </summary>
    public class VideoModel : IVideo
    {
        // ── Хранимые поля ────────────────────────────────────────────────────

        public int Id { get; set; }
        public int RelativeIndex { get; set; }
        public int AbsoluteIndex { get; set; }

        /// <summary>Очищенное отображаемое название урока (без общего префикса).</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Относительный путь к файлу (хранится в БД).
        /// При работе без БД — тоже содержит полный путь.
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>Длительность (кешируется после первого открытия MediaElement).</summary>
        public TimeSpan Duration { get; set; } = TimeSpan.Zero;

        public bool IsWatched { get; set; } = false;
        public TimeSpan LastPosition { get; set; } = TimeSpan.Zero;

        // ── Runtime-only поля (не в БД) ──────────────────────────────────────

        /// <summary>
        /// Полный абсолютный путь к файлу.
        /// Устанавливается CourseBuilderService при сканировании папки.
        /// Именно это поле передаётся в SmotrelPlayer через IVideo.FilePath.
        /// </summary>
        [NotMapped]
        public string FilePath { get; set; } = string.Empty;

        [NotMapped]
        public List<VideoTimecode> Timestamps { get; set; } = [];
    }
}