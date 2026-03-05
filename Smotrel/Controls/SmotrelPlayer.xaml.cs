using Smotrel.Enums;
using Smotrel.Interfaces;
using Smotrel.Models;
using Smotrel.Services;
using Smotrel.Settings;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Smotrel.Controls
{
    public partial class SmotrelPlayer : UserControl
    {
        // ═══════════════════════════════════════════════════════════════════
        //  Dependency Properties
        // ═══════════════════════════════════════════════════════════════════

        public static readonly DependencyProperty IsLockedProperty =
            DependencyProperty.Register(
                nameof(IsLocked), typeof(bool), typeof(SmotrelPlayer),
                new PropertyMetadata(false, OnIsLockedChanged));

        public bool IsLocked
        {
            get => (bool)GetValue(IsLockedProperty);
            set => SetValue(IsLockedProperty, value);
        }

        private static void OnIsLockedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SmotrelPlayer p)
                p.LockOverlay.Visibility = (bool)e.NewValue
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        // ── OverlayTimeout ────────────────────────────────────────────────

        public static readonly DependencyProperty OverlayTimeoutProperty =
            DependencyProperty.Register(
                nameof(OverlayTimeout), typeof(TimeSpan), typeof(SmotrelPlayer),
                new PropertyMetadata(TimeSpan.FromSeconds(3)));

        /// <summary>How long the mouse must be still before the overlay auto-hides.</summary>
        public TimeSpan OverlayTimeout
        {
            get => (TimeSpan)GetValue(OverlayTimeoutProperty);
            set => SetValue(OverlayTimeoutProperty, value);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Public Events
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Fired when the playing / paused state changes.</summary>
        public event EventHandler<PlayerState>? PlaybackStateChanged;

        /// <summary>Fired when the media reaches its natural end.</summary>
        public event EventHandler? PlaybackEnded;

        /// <summary>User pressed the Previous button or hotkey.</summary>
        public event EventHandler? PreviousRequested;

        /// <summary>User pressed the Next button or hotkey.</summary>
        public event EventHandler? NextRequested;

        /// <summary>User requested Picture-in-Picture mode (only valid in Normal mode).</summary>
        public event EventHandler? PipRequested;

        /// <summary>User requested Fullscreen mode (only valid in Normal mode).</summary>
        public event EventHandler? FullscreenRequested;

        /// <summary>User pressed the Exit-Mode button (Fullscreen/PiP back to Normal).</summary>
        public event EventHandler? ExitModeRequested;

        /// <summary>
        /// Fired ~4 times per second while media is playing so that
        /// MainPlayer can keep CourseNavigationControl in sync.
        /// </summary>
        public event EventHandler<TimeSpan>? PositionChanged;

        // ═══════════════════════════════════════════════════════════════════
        //  Private State
        // ═══════════════════════════════════════════════════════════════════

        private PlayerMode _mode = PlayerMode.Normal;
        private PlayerState _state = PlayerState.Paused;
        private VideoModel? _currentVideo;
        private PlayerSnapshot? _pendingSnap;
        private bool _mediaReady;   // true after MediaOpened fires for the current source

        // ── Seek drag ─────────────────────────────────────────────────────
        private bool _isDragging;
        private bool _wasPlayingBeforeDrag;

        // ── Volume / mute ─────────────────────────────────────────────────
        private double _volumeBeforeMute = 1.0;
        private bool _isMuted;

        // ── Overlay / cursor ──────────────────────────────────────────────
        private readonly DispatcherTimer _overlayTimer;
        private bool _mouseInactive;

        // ── Position update timer ─────────────────────────────────────────
        private readonly DispatcherTimer _posTimer;

        // ═══════════════════════════════════════════════════════════════════
        //  Constructor
        // ═══════════════════════════════════════════════════════════════════

        public SmotrelPlayer()
        {
            InitializeComponent();

            // Overlay auto-hide timer (interval overridden in Loaded once DP is set)
            _overlayTimer = new DispatcherTimer();
            _overlayTimer.Tick += OverlayTimer_Tick;

            // Position polling timer
            _posTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _posTimer.Tick += PosTimer_Tick;
            _posTimer.Start();

            // Start with overlay invisible and non-interactive
            ControlsOverlay.Opacity = 0;
            ControlsOverlay.IsHitTestVisible = false;

            // Wire ClickableProgressBar events not already hooked in XAML
            Timeline.SeekStarted += Timeline_SeekStarted;
            Timeline.SeekCompleted += Timeline_SeekCompleted;

            // Use MouseMove on the timeline track to update TbTime live during drag
            Timeline.MouseMove += Timeline_MouseMoveDrag;

            Loaded += SmotrelPlayer_Loaded;
        }

        private void SmotrelPlayer_Loaded(object sender, RoutedEventArgs e)
        {
            // Now that all DPs are applied from XAML, sync the timer interval
            _overlayTimer.Interval = OverlayTimeout;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Public Read Properties
        // ═══════════════════════════════════════════════════════════════════

        public PlayerMode Mode => _mode;
        public PlayerState PlaybackState => _state;
        public VideoModel? CurrentVideo => _currentVideo;

        // ═══════════════════════════════════════════════════════════════════
        //  Public API
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Loads a full <see cref="PlayerSnapshot"/> into this player.
        /// All playback settings are applied inside <see cref="Media_MediaOpened"/>
        /// so that MediaElement is fully ready before seeking / playing.
        /// </summary>
        public void LoadState(PlayerSnapshot snap)
        {
            _pendingSnap = snap;
            _mediaReady = false;
            _currentVideo = snap.Video;

            TbTitle.Text = snap.Video?.Title ?? string.Empty;

            // Timecodes can be applied immediately; they don't require media to be open
            if (snap.Timecodes != null)
                Timeline.Timecodes = snap.Timecodes;

            var uri = snap.Video?.FilePath is { Length: > 0 } fp
                ? new Uri(fp)
                : null;

            if(Media.Source == uri)
            {
                Media_MediaOpened(null, null);
                return;
            }
            Media.Source = uri;

            // CRITICAL: with LoadedBehavior="Manual", simply assigning Source does NOT
            // start the media pipeline — MediaOpened will never fire without this call.
            // Stop() (or Play/Pause) kicks off loading without actually playing anything;
            // the real play/pause decision is made inside Media_MediaOpened once the
            // media is fully opened and the pending snapshot can be safely applied.
            if (uri != null)
            {
                Media.Play();
            }
        }

        /// <summary>
        /// Captures a complete snapshot of the player's current state.
        /// Always call this <em>before</em> calling <see cref="LockWithMessage"/>.
        /// </summary>
        public PlayerSnapshot CaptureState() => new()
        {
            Video = _currentVideo,
            StartPos = Media.Position,
            // Store the "real" volume even when muted so it can be restored later
            Volume = _isMuted ? _volumeBeforeMute : Media.Volume,
            Speed = Media.SpeedRatio,
            State = _state,
            Timecodes = Timeline.Timecodes
        };

        /// <summary>
        /// Sets the player mode and shows/hides the appropriate overlay buttons.
        /// </summary>
        public void SetMode(PlayerMode mode)
        {
            _mode = mode;
            bool isNormal = mode == PlayerMode.Normal;

            BtnFullscreen.Visibility = isNormal ? Visibility.Visible : Visibility.Collapsed;
            BtnPip.Visibility = isNormal ? Visibility.Visible : Visibility.Collapsed;
            BtnExitMode.Visibility = isNormal ? Visibility.Collapsed : Visibility.Visible;

            // Icon: PiP uses "back to window" glyph, Fullscreen uses "exit fullscreen" glyph
            GlyphExitMode.Text = mode == PlayerMode.Pip ? "\uE9A6" : "\uE73F";
        }

        /// <summary>
        /// Pauses the video and shows the lock overlay with <paramref name="message"/>.
        /// Always call <see cref="CaptureState"/> before this so the snapshot has
        /// the correct (playing) state.
        /// </summary>
        public void LockWithMessage(string message)
        {
            TbLockMessage.Text = message;
            if (_mediaReady) Media.Pause();
            _state = PlayerState.Paused;
            GlyphPlayPause.Text = GlyphPlay;
            IsLocked = true;
        }

        /// <summary>Hides the lock overlay without starting playback.</summary>
        public void UnlockPlayer() => IsLocked = false;

        /// <summary>Toggles playing / paused.</summary>
        public void TogglePlayPause()
        {
            if (!_mediaReady) return;
            if (_state == PlayerState.Playing) DoPause();
            else DoPlay();
        }

        public void STOP()
        {
            Media.Stop();
            _state = PlayerState.Paused;
        }
        /// <summary>Seeks to an absolute <paramref name="position"/> in the current media.</summary>
        public void SeekTo(TimeSpan position)
        {
            if (!_mediaReady) return;
            var clamped = Clamp(position, TimeSpan.Zero, GetDuration());
            Media.Position = clamped;
            UpdateTimeDisplay(clamped);
            SyncTimeline(clamped);
        }

        /// <summary>
        /// Dispatches a hotkey string such as "Space" or "Shift+Right"
        /// to the appropriate internal action based on <see cref="AppSettings.Current"/>.
        /// </summary>
        public void HandleHotkey(string hotkey)
        {
            var s = AppSettings.Current;
            if (hotkey == s.HotkeyPlayPause) TogglePlayPause();
            else if (hotkey == s.HotkeySeekForward) SeekRelative(TimeSpan.FromSeconds(s.SeekForwardSeconds));
            else if (hotkey == s.HotkeySeekBackward) SeekRelative(-TimeSpan.FromSeconds(s.SeekBackwardSeconds));
            else if (hotkey == s.HotkeyNextVideo) NextRequested?.Invoke(this, EventArgs.Empty);
            else if (hotkey == s.HotkeyPrevVideo) PreviousRequested?.Invoke(this, EventArgs.Empty);
            else if (hotkey == s.HotkeyFullscreen) OnFullscreenRequested();
            else if (hotkey == s.HotkeyPiP) OnPipRequested();
            else if (hotkey == s.HotkeyEscape) ExitModeRequested?.Invoke(this, EventArgs.Empty);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Playback Helpers
        // ═══════════════════════════════════════════════════════════════════

        private const string GlyphPlay = "\uE768"; // ▶ — "click to play"
        private const string GlyphPause = "\uE769"; // ⏸ — "click to pause"

        private void DoPlay()
        {
            Media.Play();
            _state = PlayerState.Playing;
            GlyphPlayPause.Text = GlyphPause;
            PlaybackStateChanged?.Invoke(this, _state);
            ShowPlayStateAnimation(isPlaying: true);
        }

        private void DoPause()
        {
            Media.Pause();
            _state = PlayerState.Paused;
            GlyphPlayPause.Text = GlyphPlay;
            PlaybackStateChanged?.Invoke(this, _state);
            ShowPlayStateAnimation(isPlaying: false);
        }

        private void SeekRelative(TimeSpan delta)
        {
            if (!_mediaReady) return;
            var newPos = Clamp(Media.Position + delta, TimeSpan.Zero, GetDuration());
            Media.Position = newPos;
            UpdateTimeDisplay(newPos);
            SyncTimeline(newPos);
        }

        private void OnPipRequested()
        {
            if (_mode != PlayerMode.Normal) return;
            PipRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnFullscreenRequested()
        {
            if (_mode != PlayerMode.Normal) return;
            FullscreenRequested?.Invoke(this, EventArgs.Empty);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Media Events
        // ═══════════════════════════════════════════════════════════════════

        private void Media_MediaOpened(object sender, RoutedEventArgs e)
        {
            _mediaReady = true;
            if (_pendingSnap is not { } snap) return;

            var dur = GetDuration();

            Timeline.Duration = dur;

            // ── Volume ────────────────────────────────────────────────────
            _isMuted = false;
            _volumeBeforeMute = snap.Volume;
            Media.Volume = snap.Volume;
            VolumeBar.Value = snap.Volume;
            UpdateVolumeGlyph(snap.Volume);

            // ── Speed ─────────────────────────────────────────────────────
            Media.SpeedRatio = snap.Speed;
            TbSpeed.Text = FormatSpeed(snap.Speed);
            UpdateSpeedMenuChecks(snap.Speed);

            // ── Timecodes ─────────────────────────────────────────────────
            if (_currentVideo != null)
            {
                TimecodeStorage.Load(_currentVideo);         
                Timeline.Timecodes = _currentVideo.Timestamps.Count > 0
                    ? _currentVideo.Timestamps.Cast<ITimecode>().ToList()
                    : snap.Timecodes ?? [];
            }
            else if (snap.Timecodes != null)
            {
                Timeline.Timecodes = snap.Timecodes;
            }

            // ── Position ──────────────────────────────────────────────────
            var startPos = Clamp(snap.StartPos, TimeSpan.Zero, dur);

            // ── Playback state — applied LAST ─────────────────────────────
            if (snap.State == PlayerState.Playing)
            {
                Media.Play();
                Media.Position = startPos;
                _state = PlayerState.Playing;
                GlyphPlayPause.Text = GlyphPause;
            }
            else
            {
                Media.Play();
                Media.Position = startPos;
                Media.Pause();
                _state = PlayerState.Paused;
                GlyphPlayPause.Text = GlyphPlay;
            }

            UpdateTimeDisplay(startPos);
            SyncTimeline(startPos);


        }

        private void Media_MediaEnded(object sender, RoutedEventArgs e)
        {
            _state = PlayerState.Paused;
            GlyphPlayPause.Text = GlyphPlay;
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        }

        private void Media_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            _mediaReady = false;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  ClickableProgressBar / Timeline Events
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Updates the timecode list on both the progress bar and the current video model,
        /// then immediately persists the new list to the course folder.
        /// This is the single write-point for timecode edits.
        /// </summary>
        public void SetTimecodes(List<VideoTimecode> timecodes)
        {
            Timeline.Timecodes = timecodes.Cast<ITimecode>().ToList();

            if (_currentVideo == null) return;
            _currentVideo.Timestamps = timecodes;
            TimecodeStorage.Save(_currentVideo);   // → {videoName}.timecodes.json
        }

        /// <summary>
        /// Fired once when the user releases the thumb after dragging.
        /// Updates TbTime to the final value.  The actual MediaElement seek
        /// and play-state restore happen in <see cref="Timeline_SeekCompleted"/>.
        /// </summary>
        private void Timeline_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // SeekCompleted fires immediately after this on the same mouse-up;
            // just update the time text so it matches the new value before the seek completes.
            if (_isDragging)
                UpdateTimeDisplay(TimeSpan.FromSeconds(e.NewValue * GetDuration().TotalSeconds));
        }

        /// <summary>Live drag-preview: update TbTime text without touching MediaElement.Position.</summary>
        private void Timeline_MouseMoveDrag(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            var ratio = Math.Clamp(e.GetPosition(Timeline).X / Math.Max(1, Timeline.ActualWidth), 0, 1);
            UpdateTimeDisplay(TimeSpan.FromSeconds(ratio * GetDuration().TotalSeconds));
        }

        private void Timeline_SeekStarted(object? sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _isDragging = true;
            _wasPlayingBeforeDrag = _state == PlayerState.Playing;
            if (_wasPlayingBeforeDrag && _mediaReady)
                Media.Pause(); // Pause without updating _state so restore works correctly
        }

        private void Timeline_SeekCompleted(object? sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _isDragging = false;
            var targetPos = Clamp(
                TimeSpan.FromSeconds(e.NewValue * GetDuration().TotalSeconds),
                TimeSpan.Zero, GetDuration());

            if (_mediaReady)
            {
                Media.Position = targetPos;
            }

            UpdateTimeDisplay(targetPos);
            SyncTimeline(targetPos);

            // Restore play state that was active before the drag started
            if (_wasPlayingBeforeDrag && _mediaReady)
                Media.Play();
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Volume / Mute Events
        // ═══════════════════════════════════════════════════════════════════


        private void VolumeBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isMuted && e.NewValue > 0) _isMuted = false;
            Media.Volume = e.NewValue;
            if (!_isMuted && e.NewValue > 0) _volumeBeforeMute = e.NewValue;
            UpdateVolumeGlyph(e.NewValue);
        }

        private void BtnMute_Click(object sender, RoutedEventArgs e)
        {
            if (_isMuted)
            {
                _isMuted = false;
                Media.Volume = _volumeBeforeMute;
                VolumeBar.Value = _volumeBeforeMute;
            }
            else
            {
                _isMuted = true;
                if (Media.Volume > 0) _volumeBeforeMute = Media.Volume;
                Media.Volume = 0;
                VolumeBar.Value = 0;
            }
            UpdateVolumeGlyph(Media.Volume);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Speed Events
        // ═══════════════════════════════════════════════════════════════════

        private void BtnSpeed_Click(object sender, RoutedEventArgs e)
        {
            if (BtnSpeed.ContextMenu is { } cm)
            {
                cm.PlacementTarget = BtnSpeed;
                cm.IsOpen = true;
            }
        }

        private void SpeedItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi &&
                double.TryParse(mi.Tag?.ToString(),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out double speed))
            {
                Media.SpeedRatio = speed;
                TbSpeed.Text = FormatSpeed(speed);
                UpdateSpeedMenuChecks(speed);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Control Button Events
        // ═══════════════════════════════════════════════════════════════════

        private void BtnPlayPause_Click(object sender, RoutedEventArgs e) => TogglePlayPause();
        private void BtnPrev_Click(object sender, RoutedEventArgs e) => PreviousRequested?.Invoke(this, EventArgs.Empty);
        private void BtnNext_Click(object sender, RoutedEventArgs e) => NextRequested?.Invoke(this, EventArgs.Empty);
        private void BtnFullscreen_Click(object sender, RoutedEventArgs e) => OnFullscreenRequested();
        private void BtnPip_Click(object sender, RoutedEventArgs e) => OnPipRequested();
        private void BtnExitMode_Click(object sender, RoutedEventArgs e) => ExitModeRequested?.Invoke(this, EventArgs.Empty);

        private void LockOverlay_PreviewMouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;
        private void LockOverlay_PreviewKeyDown(object sender, KeyEventArgs e) => e.Handled = true;

        // ═══════════════════════════════════════════════════════════════════
        //  Overlay — Show / Hide / Auto-hide Logic
        // ═══════════════════════════════════════════════════════════════════

        private void RootGrid_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_mouseInactive)
            {
                _mouseInactive = false;
                Mouse.OverrideCursor = null;
            }
            ShowOverlay();
        }

        private void RootGrid_MouseLeave(object sender, MouseEventArgs e)
        {
            _overlayTimer.Stop();
            HideOverlay();
        }

        private void RootGrid_MouseMove(object sender, MouseEventArgs e)
        {
            // Revive from inactive state on any mouse movement
            if (_mouseInactive)
            {
                _mouseInactive = false;
                Mouse.OverrideCursor = null;
                ShowOverlay();
            }

            // Always reset the auto-hide countdown
            _overlayTimer.Stop();

            // Only start the countdown when the pointer is over the "empty" video area,
            // not when it's hovering the title bar or controls bar.
            if (!IsMouseOverInteractiveArea(e))
            {
                _overlayTimer.Interval = OverlayTimeout;
                _overlayTimer.Start();
            }
        }

        private void RootGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_mode == PlayerMode.Pip)
            {
                if (e.ClickCount == 2)
                    TogglePlayPause();
                e.Handled = true;
                return;
            }

            if (e.ClickCount == 1)
            {
                TogglePlayPause();
                e.Handled = true;
            }
        }

        private void OverlayTimer_Tick(object? sender, EventArgs e)
        {
            _overlayTimer.Stop();
            _mouseInactive = true;
            Mouse.OverrideCursor = Cursors.None;
            HideOverlay();
        }

        // ── Animation helpers ─────────────────────────────────────────────

        private void ShowOverlay()
        {
            ControlsOverlay.IsHitTestVisible = true;
            // No From property → animation starts from the current opacity value, preventing flicker
            var anim = new DoubleAnimation(1.0, TimeSpan.FromSeconds(0.15));
            ControlsOverlay.BeginAnimation(OpacityProperty, anim);
        }

        private void HideOverlay()
        {
            var anim = new DoubleAnimation(0.0, TimeSpan.FromSeconds(0.15));
            // Disable hit-testing only after the animation completes, so buttons remain
            // clickable during the fade-out.
            anim.Completed += (_, _) => ControlsOverlay.IsHitTestVisible = false;
            ControlsOverlay.BeginAnimation(OpacityProperty, anim);
        }

        /// <summary>
        /// Returns <c>true</c> when the mouse pointer is over the title bar (~50 px from top)
        /// or the controls bar (~68 px from bottom), so the auto-hide timer should not start.
        /// </summary>
        private bool IsMouseOverInteractiveArea(MouseEventArgs e)
        {
            var pt = e.GetPosition(RootGrid);
            double h = RootGrid.ActualHeight;
            return pt.Y < 50 || pt.Y > h - 68;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Position / Chapter Timer
        // ═══════════════════════════════════════════════════════════════════

        private void PosTimer_Tick(object? sender, EventArgs e)
        {
            if (!_mediaReady || _isDragging) return;
            var pos = Media.Position;

            UpdateTimeDisplay(pos);
            SyncTimeline(pos);
            UpdateChapterDisplay(pos);
            PositionChanged?.Invoke(this, pos);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Center Play / Pause Animation
        // ═══════════════════════════════════════════════════════════════════

        private void ShowPlayStateAnimation(bool isPlaying)
        {
            // Show the glyph that reflects what JUST happened
            PlayStateGlyph.Text = isPlaying ? "\uE768" : "\uE769";
            PlayStateGlyphHost.Visibility = Visibility.Visible;
            PlayStateGlyphHost.Opacity = 1.0;

            // Pop scale from 0.6 → 1.0
            var scaleDur = new Duration(TimeSpan.FromSeconds(0.2));
            var scaleEase = new CubicEase { EasingMode = EasingMode.EaseOut };
            ApplyScaleAnim(PlayStateScale, scaleDur, scaleEase);
            ApplyScaleAnim(PlayStateGlyphScale, scaleDur, scaleEase);

            // Hold at full opacity then fade out
            var fade = new DoubleAnimationUsingKeyFrames();
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.0,
                KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.0,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.35))));
            fade.KeyFrames.Add(new EasingDoubleKeyFrame(0.0,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.75)),
                new CubicEase { EasingMode = EasingMode.EaseIn }));
            fade.Completed += (_, _) => PlayStateGlyphHost.Visibility = Visibility.Collapsed;
            PlayStateGlyphHost.BeginAnimation(OpacityProperty, fade);
        }

        private static void ApplyScaleAnim(ScaleTransform t, Duration dur, IEasingFunction ease)
        {
            var ax = new DoubleAnimation(0.6, 1.0, dur) { EasingFunction = ease };
            var ay = new DoubleAnimation(0.6, 1.0, dur) { EasingFunction = ease };
            t.BeginAnimation(ScaleTransform.ScaleXProperty, ax);
            t.BeginAnimation(ScaleTransform.ScaleYProperty, ay);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Display / Helper Methods
        // ═══════════════════════════════════════════════════════════════════

        private TimeSpan GetDuration() =>
            Media.NaturalDuration.HasTimeSpan
                ? Media.NaturalDuration.TimeSpan
                : TimeSpan.Zero;

        private void UpdateTimeDisplay(TimeSpan pos)
        {
            TbTime.Text = $"{FormatTime(pos)} / {FormatTime(GetDuration())}";
        }

        /// <summary>
        /// Sets <see cref="Timeline"/>.Value from code without triggering any
        /// seek handlers — ClickableProgressBar only fires ValueChanged via RaiseEvent
        /// (not via DP change notification), so no guard flag is needed.
        /// </summary>
        private void SyncTimeline(TimeSpan pos)
        {
            var dur = GetDuration();
            Timeline.Value = dur.TotalSeconds > 0
                ? pos.TotalSeconds / dur.TotalSeconds
                : 0;
        }

        private void UpdateChapterDisplay(TimeSpan pos)
        {
            ITimecode? active = null;
            if (Timeline.Timecodes != null)
                foreach (var tc in Timeline.Timecodes)
                    if (tc.Position <= pos) active = tc;

            if (active != null)
            {
                TbChapter.Text = active.Label;
                TbChapter.Opacity = 1.0;
            }
            else
            {
                TbChapter.Opacity = 0.0;
            }
        }

        private void UpdateVolumeGlyph(double volume)
        {
            GlyphVolume.Text = volume <= 0
                ? "\uE74F"   // Muted
                : volume < 0.5
                    ? "\uE993" // Low
                    : "\uE995"; // Medium/High
        }

        private void UpdateSpeedMenuChecks(double speed)
        {
            foreach (MenuItem item in SpeedMenu.Items)
                if (double.TryParse(item.Tag?.ToString(),
                        NumberStyles.Any, CultureInfo.InvariantCulture, out double s))
                    item.IsChecked = Math.Abs(s - speed) < 0.001;
        }

        private static string FormatTime(TimeSpan t) =>
            t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
                : $"{t.Minutes}:{t.Seconds:D2}";

        private static string FormatSpeed(double speed) =>
            speed == Math.Floor(speed)
                ? $"{(int)speed}×"
                : $"{speed:0.##}×";

        private static TimeSpan Clamp(TimeSpan v, TimeSpan min, TimeSpan max) =>
            v < min ? min : v > max ? max : v;
    }
}