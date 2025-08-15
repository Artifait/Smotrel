using System;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Smotrel.Controllers
{
    public class PlayerController : IDisposable
    {
        private MediaElement? _player;
        private readonly DispatcherTimer _positionTimer;
        private bool _isPlaying;

        public event EventHandler? MediaOpened;
        public event EventHandler? MediaEnded;
        public event EventHandler<TimeSpan>? PositionChanged;
        public event EventHandler<bool>? PlayingStateChanged;

        public PlayerController()
        {
            _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _positionTimer.Tick += (s, e) =>
            {
                if (_player == null) return;
                try
                {
                    PositionChanged?.Invoke(this, _player.Position);
                }
                catch { /* swallow */ }
            };
        }

        public void Initialize(MediaElement player)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _player.MediaOpened += Player_MediaOpened;
            _player.MediaEnded += Player_MediaEnded;
            _positionTimer.Start();
            // initialize playing state as false
            _isPlaying = false;
        }

        private void Player_MediaOpened(object? sender, System.Windows.RoutedEventArgs e) => MediaOpened?.Invoke(this, EventArgs.Empty);
        private void Player_MediaEnded(object? sender, System.Windows.RoutedEventArgs e)
        {
            // set playing state false and notify
            _isPlaying = false;
            TryRaisePlayingStateChanged();
            MediaEnded?.Invoke(this, EventArgs.Empty);
        }

        public void Play()
        {
            try
            {
                if (_player == null) return;
                _player.Play();
                _isPlaying = true;
                TryRaisePlayingStateChanged();
            }
            catch { /* swallow */ }
        }

        public void Pause()
        {
            try
            {
                if (_player == null) return;
                _player.Pause();
                _isPlaying = false;
                TryRaisePlayingStateChanged();
            }
            catch { /* swallow */ }
        }

        public void Stop()
        {
            try
            {
                if (_player == null) return;
                _player.Stop();
                _isPlaying = false;
                TryRaisePlayingStateChanged();
            }
            catch { /* swallow */ }
        }

        public void Seek(TimeSpan pos) { if (_player != null) _player.Position = pos; }

        public double Speed
        {
            get => _player?.SpeedRatio ?? 1.0;
            set { if (_player != null) _player.SpeedRatio = Math.Max(0.1, Math.Min(4.0, value)); }
        }

        public double Volume
        {
            get => _player?.Volume ?? 0.5;
            set { if (_player != null) _player.Volume = Math.Max(0.0, Math.Min(1.0, value)); }
        }

        public TimeSpan Position
        {
            get => _player?.Position ?? TimeSpan.Zero;
            set { if (_player != null) _player.Position = value; }
        }

        public bool IsPlaying => _isPlaying;

        private void TryRaisePlayingStateChanged()
        {
            try { PlayingStateChanged?.Invoke(this, _isPlaying); } catch { }
        }

        public bool HasNaturalVideoSize => (_player?.NaturalVideoWidth ?? 0) > 0 && (_player?.NaturalVideoHeight ?? 0) > 0;
        public int NaturalVideoWidth => _player?.NaturalVideoWidth ?? 0;
        public int NaturalVideoHeight => _player?.NaturalVideoHeight ?? 0;

        public void Dispose()
        {
            if (_player != null)
            {
                try { _player.MediaOpened -= Player_MediaOpened; } catch { }
                try { _player.MediaEnded -= Player_MediaEnded; } catch { }
            }
            try
            {
                _positionTimer.Stop();
                _positionTimer.Tick -= (s, e) => { };
            }
            catch { }
        }
    }
}
