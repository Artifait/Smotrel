using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Smotrel.Settings
{
    /// <summary>
    /// Настройки приложения. Синглтон, сохраняется в JSON рядом с exe.
    /// Загружается при первом обращении через <see cref="Current"/>.
    /// </summary>
    public sealed class AppSettings
    {
        private static readonly string SettingsPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "smotrel_settings.json");

        private static AppSettings? _instance;

        /// <summary>Текущий экземпляр настроек (lazy load).</summary>
        public static AppSettings Current => _instance ??= Load();

        // ── Воспроизведение ───────────────────────────────────────────────────

        /// <summary>Перемотка назад (стрелка влево), секунды.</summary>
        public int SeekBackwardSeconds { get; set; } = 10;

        /// <summary>Перемотка вперёд (стрелка вправо), секунды.</summary>
        public int SeekForwardSeconds { get; set; } = 10;

        /// <summary>Таймаут до скрытия оверлея управления, секунды.</summary>
        public int OverlayTimeoutSeconds { get; set; } = 3;

        // ── Курсы ─────────────────────────────────────────────────────────────

        /// <summary>Папка по умолчанию откуда подтягиваются курсы.</summary>
        public string CoursesFolder { get; set; } = string.Empty;

        // ── Горячие клавиши (строки — допускают переопределение в UI) ─────────

        public string HotkeyPlayPause { get; set; } = "Space";
        public string HotkeySeekForward { get; set; } = "Right";
        public string HotkeySeekBackward { get; set; } = "Left";
        public string HotkeyNextVideo { get; set; } = "Shift+Right";
        public string HotkeyPrevVideo { get; set; } = "Shift+Left";
        public string HotkeyFullscreen { get; set; } = "F";
        public string HotkeyPiP { get; set; } = "P";
        public string HotkeyEscape { get; set; } = "Escape";

        // ── Персистентность ───────────────────────────────────────────────────

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json)
                           ?? new AppSettings();
                }
            }
            catch { /* файл повреждён — используем дефолты */ }

            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                var opts = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, opts));
            }
            catch { /* не критично */ }
        }

        /// <summary>Сбрасывает синглтон — используется после сохранения в SettingsWindow.</summary>
        public static void Reload() => _instance = Load();
    }
}