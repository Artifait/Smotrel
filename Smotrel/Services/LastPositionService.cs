// Services/LastPositionService.cs

using Smotrel.Models;
using System.Windows.Threading;

namespace Smotrel.Services
{
    /// <summary>
    /// Периодически сохраняет текущую позицию видео в БД.
    /// Защищает от потери прогресса при некорректном завершении.
    ///
    /// Подключение в MainPlayer:
    ///   _positionSaver = new LastPositionService(db);
    ///   _positionSaver.Start(() => (_currentVideo, _activePlayer.CurrentTime));
    ///
    /// При закрытии:
    ///   _positionSaver.FlushAndStop();
    /// </summary>
    public sealed class LastPositionService : IDisposable
    {
        private readonly SmotrelContext _db;
        private readonly DispatcherTimer _timer;
        private Func<(CourseModel? course, VideoModel? video, TimeSpan pos)>? _stateGetter;
        private VideoModel? _lastSaved;
        private TimeSpan _lastSavedPos;

        private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(8);

        public LastPositionService(SmotrelContext db)
        {
            _db = db;
            _timer = new DispatcherTimer { Interval = SaveInterval };
            _timer.Tick += OnTick;
        }

        public void Start(Func<(CourseModel? course, VideoModel?, TimeSpan)> stateGetter)
        {
            _stateGetter = stateGetter;
            _timer.Start();
        }

        private async void OnTick(object? sender, EventArgs e)
        {
            if (_stateGetter == null) return;
            var (course, video, pos) = _stateGetter();
            await TrySaveAsync(course, video, pos);
        }

        /// <summary>Вызвать явно при закрытии окна / смене видео.</summary>
        public async Task FlushAndStop()
        {
            _timer.Stop();
            if (_stateGetter == null) return;
            var (course, video, pos) = _stateGetter();
            await TrySaveAsync(course, video, pos);
        }

        private async Task TrySaveAsync(CourseModel? course, VideoModel? video, TimeSpan pos)
        {
            if (video == null) return;
            if (course == null) return;

            if (_lastSaved?.Id == video.Id &&
                Math.Abs((_lastSavedPos - pos).TotalSeconds) < 2) return;

            try
            {
                video.LastPosition = pos;
                course.LastVideoId = video.Id;
                _db.Entry(video).Property(v => v.LastPosition).IsModified = true;
                _db.Entry(course).Property(c => c.LastVideoId).IsModified = true;
                await _db.SaveChangesAsync();

                _lastSaved = video;
                _lastSavedPos = pos;
            }
            catch {  }
        }

        public void Dispose() => FlushAndStop().GetAwaiter().GetResult();
    }
}