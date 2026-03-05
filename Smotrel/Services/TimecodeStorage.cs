using Smotrel.Models;
using System.IO;
using System.Text.Json;

namespace Smotrel.Services
{
    /// <summary>
    /// Сохраняет и загружает таймкоды видеоурока в JSON-файл,
    /// расположенный в папке курса рядом с самим видеофайлом.
    ///
    /// Имя файла: {videoFileNameWithoutExtension}.timecodes.json
    /// Например:  C:\Courses\Python\02_variables.timecodes.json
    /// </summary>
    public static class TimecodeStorage
    {
        private static readonly JsonSerializerOptions _json = new()
        {
            WriteIndented = true
        };

        // ── Публичный API ────────────────────────────────────────────────────

        /// <summary>
        /// Сохраняет <see cref="VideoModel.Timestamps"/> в файл.
        /// Ничего не делает, если <see cref="VideoModel.FilePath"/> пуст.
        /// </summary>
        public static void Save(VideoModel video)
        {
            var path = GetStoragePath(video);
            if (path is null) return;

            try
            {
                var json = JsonSerializer.Serialize(video.Timestamps, _json);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                // Запись таймкодов — некритичная операция; логируем и идём дальше.
                System.Diagnostics.Debug.WriteLine(
                    $"[TimecodeStorage] Save failed for '{path}': {ex.Message}");
            }
        }

        /// <summary>
        /// Читает таймкоды из файла и записывает их в <see cref="VideoModel.Timestamps"/>.
        /// Если файла нет или он повреждён — оставляет список пустым.
        /// </summary>
        public static void Load(VideoModel video)
        {
            var path = GetStoragePath(video);
            if (path is null || !File.Exists(path)) return;

            try
            {
                var json = File.ReadAllText(path);
                var list = JsonSerializer.Deserialize<List<VideoTimecode>>(json);
                video.Timestamps = list ?? [];
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[TimecodeStorage] Load failed for '{path}': {ex.Message}");
                video.Timestamps = [];
            }
        }

        // ── Приватные хелперы ────────────────────────────────────────────────

        /// <returns>
        /// Полный путь к файлу таймкодов, или <c>null</c> если <c>FilePath</c> не задан.
        /// </returns>
        private static string? GetStoragePath(VideoModel video)
        {
            if (string.IsNullOrEmpty(video.FilePath)) return null;

            var dir = Path.GetDirectoryName(video.FilePath);
            var name = Path.GetFileNameWithoutExtension(video.FilePath);

            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name)) return null;

            return Path.Combine(dir, $"{name}.timecodes.json");
        }
    }
}