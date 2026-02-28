using Smotrel.Models;
using System.IO;

namespace Smotrel.Services
{
    /// <summary>
    /// Строит <see cref="CourseModel"/> из папки на диске.
    ///
    /// Два режима:
    ///   <see cref="Build"/>      — синхронный, без прогресса (быстро, без длительностей)
    ///   <see cref="BuildAsync"/> — асинхронный с <see cref="IProgress{T}"/> (читает длительности)
    ///
    /// Изменения v2:
    ///   • BuildAsync: первый проход — сканирование структуры (мгновенно),
    ///     второй проход — чтение длительностей с репортингом прогресса по каждому файлу.
    ///   • Структура строится без длительностей (быстро), потом длительности заполняются.
    /// </summary>
    public class CourseBuilderService
    {
        private static readonly HashSet<string> VideoExtensions =
            new(StringComparer.OrdinalIgnoreCase)
            { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v", ".flv", ".webm", ".ts", ".mpg", ".mpeg" };

        // ── Синхронный (без длительностей, быстрый) ──────────────────────────

        /// <summary>
        /// Быстрое построение курса без чтения длительностей.
        /// Duration = TimeSpan.Zero — заполнится в MediaOpened плеера.
        /// </summary>
        public CourseModel Build(string rootPath)
        {
            if (!Directory.Exists(rootPath))
                throw new DirectoryNotFoundException($"Папка не найдена: {rootPath}");

            var root = new DirectoryInfo(rootPath);
            var allRawNames = CollectAllRawNames(root);
            var cleanMap = LabelCleaner.BuildCleanMap(allRawNames);
            var counter = new Counter();

            var mainChapter = BuildChapter(root, 0, counter,
                durations: null, cleanMap);

            return new CourseModel
            {
                Label = GetClean(cleanMap, root.Name),
                MainChapter = mainChapter,
            };
        }

        // ── Асинхронный с прогрессом (читает длительности) ───────────────────

        /// <summary>
        /// Строит курс асинхронно.
        /// Фаза 1: сканирование структуры папок (мгновенно, на UI-потоке безопасно).
        /// Фаза 2: чтение длительностей каждого файла с репортингом прогресса.
        /// </summary>
        /// <param name="rootPath">Путь до корневой папки курса.</param>
        /// <param name="progress">Прогресс: (обработано, всего, текущий файл).</param>
        /// <param name="ct">Токен отмены.</param>
        public async Task<CourseModel> BuildAsync(
            string rootPath,
            IProgress<CourseBuilderProgress>? progress = null,
            CancellationToken ct = default)
        {
            if (!Directory.Exists(rootPath))
                throw new DirectoryNotFoundException($"Папка не найдена: {rootPath}");

            var root = new DirectoryInfo(rootPath);

            // ── Фаза 1: структура (без длительностей, быстро) ─────────────────
            var allRawNames = CollectAllRawNames(root);
            var cleanMap = LabelCleaner.BuildCleanMap(allRawNames);
            var counter = new Counter();

            var mainChapter = BuildChapter(root, 0, counter,
                durations: null, cleanMap);

            var course = new CourseModel
            {
                Label = GetClean(cleanMap, root.Name),
                MainChapter = mainChapter,
            };

            // ── Фаза 2: длительности (медленно, с прогрессом) ─────────────────
            var allVideos = CollectAllVideos(mainChapter);
            int total = allVideos.Count;
            int current = 0;

            // Читаем по одному с репортингом, параллелизм внутри VideoDurationReader
            // Здесь intentionally sequential — чтобы прогресс был линейным и
            // пользователь видел файл за файлом, а не "всё сразу"
            await Task.Run(() =>
            {
                foreach (var video in allVideos)
                {
                    ct.ThrowIfCancellationRequested();

                    progress?.Report(new CourseBuilderProgress(current, total, video.FilePath));

                    video.Duration = VideoDurationReader.Read(video.FilePath);
                    current++;
                }

                progress?.Report(new CourseBuilderProgress(total, total, string.Empty));
            }, ct);

            return course;
        }

        // ── Построение дерева ─────────────────────────────────────────────────

        private static ChapterCourseModel BuildChapter(
            DirectoryInfo dir,
            int chapterIndex,
            Counter counter,
            Dictionary<string, TimeSpan>? durations,
            Dictionary<string, string> cleanMap)
        {
            var chapter = new ChapterCourseModel
            {
                Title = GetClean(cleanMap, dir.Name),
                RelativeIndex = chapterIndex,
            };

            // Видео в текущей папке
            var files = dir
                .GetFiles()
                .Where(f => VideoExtensions.Contains(f.Extension))
                .OrderBy(f => f.Name, NaturalSortComparer.Instance)
                .ToList();

            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                var nameNoExt = Path.GetFileNameWithoutExtension(f.Name);

                var video = new VideoModel
                {
                    RelativeIndex = i,
                    AbsoluteIndex = counter.Next(),
                    Title = GetClean(cleanMap, nameNoExt),
                    Path = f.FullName,
                    FilePath = f.FullName,
                    Duration = durations != null && durations.TryGetValue(f.FullName, out var d)
                                    ? d : TimeSpan.Zero,
                };

                chapter.Videos.Add(video);
            }

            // Подглавы
            var subDirs = dir
                .GetDirectories()
                .OrderBy(d => d.Name, NaturalSortComparer.Instance)
                .ToList();

            for (int i = 0; i < subDirs.Count; i++)
            {
                chapter.Chapters.Add(
                    BuildChapter(subDirs[i], i, counter, durations, cleanMap));
            }

            return chapter;
        }

        // ── Вспомогательные методы ────────────────────────────────────────────

        private static List<string> CollectAllRawNames(DirectoryInfo root)
        {
            var names = new List<string> { root.Name };

            void Collect(DirectoryInfo d)
            {
                foreach (var sub in d.GetDirectories())
                { names.Add(sub.Name); Collect(sub); }

                foreach (var f in d.GetFiles())
                    if (VideoExtensions.Contains(f.Extension))
                        names.Add(Path.GetFileNameWithoutExtension(f.Name));
            }

            Collect(root);
            return names;
        }

        private static List<VideoModel> CollectAllVideos(ChapterCourseModel chapter)
        {
            var list = new List<VideoModel>();
            list.AddRange(chapter.Videos);
            foreach (var ch in chapter.Chapters)
                list.AddRange(CollectAllVideos(ch));
            return list;
        }

        private static string GetClean(Dictionary<string, string> map, string raw)
            => map.TryGetValue(raw, out var clean) ? clean : raw;

        private sealed class Counter
        {
            private int _v = -1;
            public int Next() => Interlocked.Increment(ref _v);
        }
    }
}