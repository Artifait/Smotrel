using System.IO;
using System.Windows.Media;
using System.Windows.Threading;

namespace Smotrel.Services
{
    /// <summary>
    /// Читает длительность видеофайла через WPF MediaPlayer.
    ///
    /// MediaPlayer требует STA-потока — запускается в отдельном потоке
    /// с собственным Dispatcher, UI при этом не блокируется.
    ///
    /// Если файл недоступен или не является видео — возвращает TimeSpan.Zero.
    /// </summary>
    public static class VideoDurationReader
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

        /// <summary>
        /// Считывает длительность одного файла.
        /// Вызов синхронный с точки зрения вызывающего, но внутри STA-поток.
        /// </summary>
        public static TimeSpan Read(string filePath)
        {
            if (!File.Exists(filePath)) return TimeSpan.Zero;

            TimeSpan result   = TimeSpan.Zero;
            var done          = new ManualResetEventSlim(false);

            // MediaPlayer обязан жить в STA-потоке с собственным Dispatcher
            var thread = new Thread(() =>
            {
                var player = new MediaPlayer();
                var localDispatcher = Dispatcher.CurrentDispatcher;

                player.MediaOpened += (_, _) =>
                {
                    result = player.NaturalDuration.HasTimeSpan
                        ? player.NaturalDuration.TimeSpan
                        : TimeSpan.Zero;
                    player.Close();
                    localDispatcher.InvokeShutdown();
                    done.Set();
                };

                player.MediaFailed += (_, _) =>
                {
                    localDispatcher.InvokeShutdown();
                    done.Set();
                };

                player.Open(new Uri(filePath, UriKind.Absolute));

                // Запускаем Dispatcher — он обработает MediaOpened/MediaFailed
                Dispatcher.Run();
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            // Ждём завершения с таймаутом
            if (!done.Wait(Timeout))
            {
                // Файл завис — возвращаем Zero, не блокируем сборку курса
                thread.Interrupt();
            }

            return result;
        }

        /// <summary>
        /// Считывает длительность всех файлов из списка параллельно.
        /// Возвращает словарь filePath → duration.
        /// </summary>
        public static Dictionary<string, TimeSpan> ReadBatch(IEnumerable<string> filePaths)
        {
            var paths  = filePaths.ToList();
            var result = new Dictionary<string, TimeSpan>(paths.Count, StringComparer.OrdinalIgnoreCase);

            // Параллельно, но каждый вызов Read() уже в своём STA-потоке
            Parallel.ForEach(paths, path =>
            {
                var duration = Read(path);
                lock (result)
                    result[path] = duration;
            });

            return result;
        }
    }
}
