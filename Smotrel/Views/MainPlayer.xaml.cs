using Smotrel.Controls;
using Smotrel.DialogWindows;
using Smotrel.Enums;
using Smotrel.Interfaces;
using Smotrel.Models;
using Smotrel.Services;     
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;

namespace Smotrel.Views
{
    public partial class MainPlayer : Window
    {
        // ── Fields ──────────────────────────────────────────────────────────
        private readonly PlayerSnapshot? _initialSnapshot;

        private readonly CourseModel _course;
        private readonly SmotrelContext _db;
        private readonly LastPositionService _lastPosSvc;

        private VideoModel? _currentVideo;
        private bool _isFullscreen;
        private PipPlayerWindow? _pipWindow;

        /// <summary>The player that should receive hotkeys at any given moment.</summary>
        private SmotrelPlayer ActivePlayer =>
            _isFullscreen ? PlayerFullscreen : PlayerNormal;

        // ═══════════════════════════════════════════════════════════════════
        //  Constructor
        // ═══════════════════════════════════════════════════════════════════

        public MainPlayer(CourseModel course, SmotrelContext db, PlayerSnapshot? snapshot = null)
        {
            InitializeComponent();

            _initialSnapshot = snapshot;
            _course = course;
            _db = db;

            GapTitle.Text = course.Label;

            // ── Configure each player ──────────────────────────────────────
            PlayerNormal.SetMode(PlayerMode.Normal);

            // PlayerFullscreen starts locked (set in XAML); just set the mode
            PlayerFullscreen.SetMode(PlayerMode.Fullscreen);

            // ── Wire player events ─────────────────────────────────────────
            WireCommonPlayerEvents(PlayerNormal);
            WireCommonPlayerEvents(PlayerFullscreen);

            // Mode-specific events
            PlayerNormal.PipRequested += PlayerNormal_PipRequested;
            PlayerNormal.FullscreenRequested += PlayerNormal_FullscreenRequested;
            PlayerFullscreen.ExitModeRequested += PlayerFullscreen_ExitModeRequested;

            // ── Nav → Player position sync ─────────────────────────────────
            PlayerNormal.PositionChanged += (_, pos) =>
            {
                if (!_isFullscreen) Nav.UpdatePosition(pos);
            };
            PlayerFullscreen.PositionChanged += (_, pos) =>
            {
                if (_isFullscreen) Nav.UpdatePosition(pos);
            };

            // ── Course navigation control ──────────────────────────────────
            Nav.Initialize(course, db);

            // ── Last-position persistence service ─────────────────────────
            _lastPosSvc = new LastPositionService(db);
            _lastPosSvc.Start(() => (_course, _currentVideo, ActivePlayer.CaptureState().StartPos));

            // ── Load the first video after the window is shown ─────────────
            VideoModel? startVideo = null;
            if (_initialSnapshot != null)
            {
                startVideo = _initialSnapshot.Video;
            } else {
                var firstVideo = course.GetVideoByAbsoluteIndex(0);
                startVideo = course.LastVideo ?? firstVideo;
            }

            Loaded += async (_, _) => await LoadVideoWithResumeAsync(startVideo!, true);
        }

        // ── Wire helper ───────────────────────────────────────────────────
        private void WireCommonPlayerEvents(SmotrelPlayer player)
        {
            player.PlaybackEnded += Player_PlaybackEnded;
            player.PreviousRequested += Player_PreviousRequested;
            player.NextRequested += Player_NextRequested;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Video Loading with Resume Dialog
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Loads <paramref name="video"/> into the active player.
        /// Shows the resume dialog when the video has a saved position.
        /// </summary>
        private async Task LoadVideoWithResumeAsync(VideoModel video, bool showResumeDialog = false)
        {
            OnWindowStateChanged(null, null);
            _currentVideo = video;
            Nav.SetCurrentVideo(video);

            TimeSpan startPos = TimeSpan.Zero;
            if(_initialSnapshot != null)
            {
                ActivePlayer.LoadState(_initialSnapshot);
                await Task.CompletedTask;
                return;
            }
            else
            {
                // Show resume dialog only when there's a meaningful saved position
                if (video.LastPosition > TimeSpan.FromSeconds(2) && showResumeDialog)
                {
                    var dlg = new ResumeDialog(video.Title, video.LastPosition,
                        Nav.ActualWidth, PlayerNormal.ActualWidth, 
                        DamperGap.ActualHeight, GapDefinition.ActualHeight,
                        PlayerNormal.ActualHeight)
                    {
                        Owner = this,
                    };

                    dlg.ShowDialog();
                    if (dlg.ShouldResume)
                        startPos = video.LastPosition;
                }
            }
            
            // Inherit current volume / speed from whichever player is active so the
            // transition feels seamless when switching videos.
            var current = ActivePlayer.CaptureState();

            var snap = new PlayerSnapshot
            {
                Video = video,
                StartPos = startPos,
                Volume = current.Video != null ? current.Volume : 1.0,
                Speed = current.Video != null ? current.Speed : 1.0,
                State = PlayerState.Playing,
                Timecodes = video.Timestamps.Cast<ITimecode>().ToList()
            };

            ActivePlayer.LoadState(snap);
            
            await Task.CompletedTask; // keeps the method async for future awaits
        }

        private static VideoModel? FindResumeVideo(ChapterCourseModel chapter)
        {
            foreach (var v in chapter.Videos)
                if (!v.IsWatched && v.LastPosition > TimeSpan.Zero)
                    return v;

            foreach (var sub in chapter.Chapters)
            {
                var found = FindResumeVideo(sub);
                if (found != null) return found;
            }

            return null;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  XAML Event Handlers — CourseNavigationControl
        // ═══════════════════════════════════════════════════════════════════
        private void Nav_VideoRequested(VideoModel video)
            => _ = LoadVideoWithResumeAsync(video);

        private void Nav_SeekRequested(TimeSpan pos)
            => ActivePlayer.SeekTo(pos);

        private void Nav_TimecodeChanged(VideoModel video)
        {
            if (_currentVideo?.Id == video.Id)
            {
                ActivePlayer.SetTimecodes(video.Timestamps);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  XAML Event Handlers — SmotrelPlayer (both)
        // ═══════════════════════════════════════════════════════════════════

        private void Player_PlaybackStateChanged(object? sender, PlayerState state)
        {
            // Nothing to update in the MainPlayer UI at the moment.
            // Could update a taskbar overlay icon here if desired.
        }

        private void Player_PlaybackEnded(object? sender, EventArgs e)
        {
            if (_currentVideo == null) return;
            var next = _course.GetNextVideo(_currentVideo);
            if (next != null) _ = LoadVideoWithResumeAsync(next);
        }

        private void Player_PreviousRequested(object? sender, EventArgs e)
        {
            if (_currentVideo == null) return;
            var prev = _course.GetPreviousVideo(_currentVideo);
            if (prev != null) _ = LoadVideoWithResumeAsync(prev);
        }

        private void Player_NextRequested(object? sender, EventArgs e)
        {
            if (_currentVideo == null) return;
            var next = _course.GetNextVideo(_currentVideo);
            if (next != null) _ = LoadVideoWithResumeAsync(next);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Picture-in-Picture
        // ═══════════════════════════════════════════════════════════════════

        private void PlayerNormal_PipRequested(object? sender, EventArgs e)
        {
            // ① Capture the full state BEFORE locking (so State = Playing is preserved)
            var snap = PlayerNormal.CaptureState();

            // ② Lock PlayerNormal — pauses media and shows overlay
            PlayerNormal.LockWithMessage("Playing in Picture-in-Picture");

            // ③ Open PiP window — it owns the snapshot and handles its player
            _pipWindow = new PipPlayerWindow(this, this._course, snap, _db);
            _pipWindow.Closed += PipWindow_Closed;
            _pipWindow.Show();
        }

        /// <summary>
        /// Called by <see cref="PipPlayerWindow"/> when the user explicitly returns
        /// to normal mode (via the exit-mode button inside PiP).
        /// </summary>
        public void ReturnFromPip(PlayerSnapshot snap)
        {
            _pipWindow = null;
            PlayerNormal.UnlockPlayer();
            PlayerNormal.LoadState(snap); // full state restore — IMPORTANT (2)
        }

        private void PipWindow_Closed(object? sender, EventArgs e)
        {
            // Window was closed without calling ReturnFromPip (e.g. Alt+F4, OS close)
            if (_pipWindow != null && PlayerNormal.IsLocked)
            {
                _pipWindow = null;
                PlayerNormal.UnlockPlayer();

                // Resume at the last known position in a paused state
                if (_currentVideo != null)
                    PlayerNormal.LoadState(PlayerSnapshot.Default(_currentVideo) with
                    {
                        State = PlayerState.Paused
                    });
            }
            _pipWindow = null;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Fullscreen
        // ═══════════════════════════════════════════════════════════════════
        private bool IsInFullscreenMode = false;
        private void PlayerNormal_FullscreenRequested(object? sender, EventArgs e)
        {
            //if(!IsInFullscreenMode)
            //{
            //    Grid oldParent = (VisualTreeHelper.GetParent(PlayerNormal) as Grid)!;
            //    oldParent.Children.Remove(PlayerNormal);

            //    FullscreenLayout.Children.Add(PlayerNormal);
            //    NormalLayout.Visibility = Visibility.Collapsed;
            //    FullscreenLayout.Visibility = Visibility.Visible;
            //    IsInFullscreenMode = true;
            //}
            //else
            //{
            //    Grid oldParent = (VisualTreeHelper.GetParent(PlayerNormal) as Grid)!;
            //    oldParent.Children.Remove(PlayerNormal);

            //    NormalLayout.Children.Add(PlayerNormal);
            //    FullscreenLayout.Visibility = Visibility.Collapsed;
            //    NormalLayout.Visibility = Visibility.Visible;
            //    IsInFullscreenMode = false;
            //}


            // ① Capture state from Normal BEFORE locking — IMPORTANT (2)
            var snap = PlayerNormal.CaptureState();

            // ② Lock Normal player
            PlayerNormal.LockWithMessage("Playing in Fullscreen");

            // ③ Unlock and configure the Fullscreen player with captured state
            PlayerFullscreen.UnlockPlayer();
            PlayerFullscreen.LoadState(snap);

            // ④ Switch layouts
            NormalLayout.Visibility = Visibility.Collapsed;
            FullscreenLayout.Visibility = Visibility.Visible;
            _isFullscreen = true;

            WindowState = WindowState.Maximized;
        }

        private void PlayerFullscreen_ExitModeRequested(object? sender, EventArgs e)
            => ExitFullscreen();

        private void ExitFullscreen()
        {
            // ① Capture state from Fullscreen player — IMPORTANT (2)
            var snap = PlayerFullscreen.CaptureState();

            // ② Lock Fullscreen player (hides it after layout swap)
            PlayerFullscreen.LockWithMessage(string.Empty);

            // ③ Restore Normal player from captured state
            PlayerNormal.UnlockPlayer();
            PlayerNormal.LoadState(snap);

            // ④ Switch layouts
            FullscreenLayout.Visibility = Visibility.Collapsed;
            NormalLayout.Visibility = Visibility.Visible;
            _isFullscreen = false;

        }

        // ═══════════════════════════════════════════════════════════════════
        //  Keyboard Hotkeys
        // ═══════════════════════════════════════════════════════════════════

        private void MainPlayer_KeyDown(object sender, KeyEventArgs e)
        {
            // Do not forward hotkeys when PiP is active — PiP has its own focus
            if (_pipWindow != null && !_isFullscreen) return;

            if (Keyboard.FocusedElement is TextBox or TextBlock) return;

            var hotkey = BuildHotkeyString(e.Key, Keyboard.Modifiers);
            ActivePlayer.HandleHotkey(hotkey);
            e.Handled = true;
        }

        /// <summary>
        /// Converts a WPF <see cref="Key"/> + <see cref="ModifierKeys"/> combo
        /// into the same string format stored in <see cref="AppSettings"/>
        /// (e.g. "Space", "Shift+Right", "Ctrl+F").
        /// </summary>
        private static string BuildHotkeyString(Key key, ModifierKeys mods)
        {
            var sb = new System.Text.StringBuilder();
            if (mods.HasFlag(ModifierKeys.Control)) sb.Append("Ctrl+");
            if (mods.HasFlag(ModifierKeys.Shift)) sb.Append("Shift+");
            if (mods.HasFlag(ModifierKeys.Alt)) sb.Append("Alt+");
            sb.Append(key.ToString());
            return sb.ToString();
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Window Chrome Events
        // ═══════════════════════════════════════════════════════════════════

        private void Header_Down(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e) => Close();
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;

        /// <summary>
        /// Compensates for WPF maximized-window overscan (adds a small top gap so
        /// the window doesn't bleed under the taskbar).
        /// </summary>
        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            var tmplExit = ExitBtn.Template;
            var tmplBack = BackBtn.Template;
            var borderExit = tmplExit.FindName("RootBorder", ExitBtn) as System.Windows.Controls.Border;
            var borderBack = tmplBack.FindName("RootBorder", BackBtn) as System.Windows.Controls.Border;

            if (WindowState == WindowState.Maximized)
            {
                ColumnOfWindowManagmentBtns.Width = new GridLength(120);
                exitBtnDamper.Width = new GridLength(5);
                backBtnDamper.Width = new GridLength(5);
                DamperGap.Height = new GridLength(5);
                if (borderExit != null) { borderExit.CornerRadius = new CornerRadius(0); borderExit.Width = 40; }
                if (borderBack != null) borderBack.CornerRadius = new CornerRadius(0);
            }
            else
            {
                exitBtnDamper.Width = new GridLength(0);
                backBtnDamper.Width = new GridLength(0);
                DamperGap.Height = new GridLength(0);
                if (borderExit != null) borderExit.CornerRadius = new CornerRadius(0, 10, 0, 0);
                if (borderBack != null) borderBack.CornerRadius = new CornerRadius(10, 0, 0, 0);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Shutdown
        // ═══════════════════════════════════════════════════════════════════

        protected override async void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            // Gracefully close PiP without the ReturnFromPip path
            _pipWindow?.Close();

            new MainWindow().Show();
            // Flush the last known position to the database before we exit
            await _lastPosSvc.FlushAndStop();

        }
    }
}