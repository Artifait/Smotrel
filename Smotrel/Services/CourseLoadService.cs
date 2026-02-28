using Microsoft.EntityFrameworkCore;
using Smotrel.Models;

namespace Smotrel.Services
{
    /// <summary>
    /// Отвечает за загрузку <see cref="CourseModel"/> из базы данных.
    ///
    /// Решает две задачи:
    ///   1. Инкрементальная загрузка дерева глав/видео (EF lazy-load дерева).
    ///   2. Восстановление runtime-полей после загрузки из БД:
    ///      VideoModel.FilePath = VideoModel.Path (полный путь, хранится в БД,
    ///      но не маппится обратно в [NotMapped] FilePath автоматически).
    /// </summary>
    public class CourseLoadService(SmotrelContext context)
    {
        private readonly SmotrelContext _ctx = context;

        // ── Публичный API ─────────────────────────────────────────────────────

        /// <summary>
        /// Загружает курс по Id вместе со всем деревом глав и видео.
        /// После загрузки восстанавливает FilePath для всех VideoModel.
        /// </summary>
        /// <returns>
        /// Готовый <see cref="CourseModel"/> с заполненным FilePath,
        /// или <c>null</c> если курс не найден.
        /// </returns>
        public CourseModel? Load(int courseId)
        {
            // Шаг 1: Загружаем корень курса + корневую главу
            var course = _ctx.Courses
                .Include(c => c.MainChapter)
                .AsSplitQuery()
                .SingleOrDefault(c => c.Id == courseId);

            if (course?.MainChapter == null) return null;

            // Шаг 2: Рекурсивно загружаем всё дерево
            LoadChapterTree(course.MainChapter);

            // Шаг 3: Восстанавливаем [NotMapped] FilePath = Path для каждого видео
            PopulateFilePaths(course.MainChapter);

            return course;
        }

        /// <summary>
        /// Асинхронная версия <see cref="Load"/>.
        /// </summary>
        public async Task<CourseModel?> LoadAsync(int courseId,
            CancellationToken ct = default)
        {
            var course = await _ctx.Courses
                .Include(c => c.MainChapter)
                .AsSplitQuery()
                .SingleOrDefaultAsync(c => c.Id == courseId, ct);

            if (course?.MainChapter == null) return null;

            await LoadChapterTreeAsync(course.MainChapter, ct);
            PopulateFilePaths(course.MainChapter);

            return course;
        }

        // ── EF: загрузка дерева глав ──────────────────────────────────────────

        /// <summary>
        /// Рекурсивно загружает Videos и Chapters для каждой главы.
        /// Синхронная версия.
        /// </summary>
        private void LoadChapterTree(ChapterCourseModel chapter)
        {
            _ctx.Entry(chapter)
                .Collection(c => c.Videos)
                .Query()
                .OrderBy(v => v.RelativeIndex)
                .Load();

            _ctx.Entry(chapter)
                .Collection(c => c.Chapters)
                .Query()
                .OrderBy(c => c.RelativeIndex)
                .Load();

            foreach (var child in chapter.Chapters)
                LoadChapterTree(child);
        }

        /// <summary>
        /// Рекурсивно загружает Videos и Chapters для каждой главы.
        /// Асинхронная версия.
        /// </summary>
        private async Task LoadChapterTreeAsync(ChapterCourseModel chapter,
            CancellationToken ct)
        {
            await _ctx.Entry(chapter)
                .Collection(c => c.Videos)
                .Query()
                .OrderBy(v => v.RelativeIndex)
                .LoadAsync(ct);

            await _ctx.Entry(chapter)
                .Collection(c => c.Chapters)
                .Query()
                .OrderBy(c => c.RelativeIndex)
                .LoadAsync(ct);

            foreach (var child in chapter.Chapters)
                await LoadChapterTreeAsync(child, ct);
        }

        private static void PopulateFilePaths(ChapterCourseModel chapter)
        {
            foreach (var video in chapter.Videos)
            {
                // Path содержит полный путь, сохранённый CourseBuilderService
                video.FilePath = video.Path;
            }

            foreach (var child in chapter.Chapters)
                PopulateFilePaths(child);
        }
    }
}