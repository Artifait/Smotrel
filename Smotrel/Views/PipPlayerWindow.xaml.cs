using Smotrel.Controls;
using Smotrel.DialogWindows;
using Smotrel.Enums;
using Smotrel.Interfaces;
using Smotrel.Models;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Smotrel.Views
{
    /// <summary>
    /// PiP-окно с отдельным SmotrelPlayer.
    /// Перетаскивается за любую точку.
    /// При закрытии активирует MainPlayer.
    /// </summary>
    public partial class PipPlayerWindow : Window
    {
        #region AddFunctForResizeWindow
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        private const int WM_NCHITTEST = 0x0084;

        private const int RESIZE_BORDER = 8;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
        }

        private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_NCHITTEST)
            {
                Point p = PointFromLParam(lParam);

                double width = ActualWidth;
                double height = ActualHeight;

                if (p.X <= RESIZE_BORDER && p.Y <= RESIZE_BORDER) { handled = true; return (IntPtr)HTTOPLEFT; }
                if (p.X >= width - RESIZE_BORDER && p.Y <= RESIZE_BORDER) { handled = true; return (IntPtr)HTTOPRIGHT; }
                if (p.X <= RESIZE_BORDER && p.Y >= height - RESIZE_BORDER) { handled = true; return (IntPtr)HTBOTTOMLEFT; }
                if (p.X >= width - RESIZE_BORDER && p.Y >= height - RESIZE_BORDER) { handled = true; return (IntPtr)HTBOTTOMRIGHT; }

                if (p.Y <= RESIZE_BORDER) { handled = true; return (IntPtr)HTTOP; }
                if (p.Y >= height - RESIZE_BORDER) { handled = true; return (IntPtr)HTBOTTOM; }
                if (p.X <= RESIZE_BORDER) { handled = true; return (IntPtr)HTLEFT; }
                if (p.X >= width - RESIZE_BORDER) { handled = true; return (IntPtr)HTRIGHT; }
            }

            return IntPtr.Zero;
        }


        private Point PointFromLParam(IntPtr lParam)
        {
            int x = unchecked((short)(long)lParam);
            int y = unchecked((short)((long)lParam >> 16));

            return this.PointFromScreen(new Point(x, y));
        }
        #endregion
        // ── Fields ──────────────────────────────────────────────────────────
        private MainPlayer _owner;
        private CourseModel _course;
        private SmotrelContext _db;
        private VideoModel _currentVideo;

        // ── Drag-threshold state ─────────────────────────────────────────
        // A single click on the window should NOT accidentally toggle play/pause
        // when the user is trying to drag the window.  We require a double-click
        // for toggle and use a pixel threshold to distinguish drag from click.
        private Point _mouseDownPos;
        private bool _potentialDrag;
        private const double DragThreshold = 6.0; // pixels

        // ── Constructor ─────────────────────────────────────────────────────
        public PipPlayerWindow(MainPlayer owner, CourseModel course, PlayerSnapshot initialSnap, SmotrelContext context)
        {
            InitializeComponent();

            _course = course;
            _owner = owner;
            _db = context;
            _currentVideo = initialSnap.Video!;

            // Put the internal SmotrelPlayer into PiP mode:
            // • hides the Fullscreen and PiP buttons
            // • shows the ExitMode (back-to-Normal) button
            PipPlayer.SetMode(PlayerMode.Pip);

            // Apply the full snapshot — position, volume, speed, state, timecodes —
            // everything is applied inside Media_MediaOpened (IMPORTANT 1).
            PipPlayer.LoadState(initialSnap);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Window Drag Logic
        //
        //  Behaviour:
        //    • Single press + move > DragThreshold  →  drag window (DragMove)
        //    • Double-click (ClickCount == 2)        →  toggle play/pause
        //    • Single click without drag             →  no action (ignored)
        //
        //  DragMove() is a blocking Win32 call; after it returns the mouse
        //  button has already been released, so no MouseLeftButtonUp fires.
        //  We therefore reset _potentialDrag inside the move handler before
        //  calling DragMove.
        // ═══════════════════════════════════════════════════════════════════

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Double-click → toggle play/pause, do not start a drag
            if (e.ClickCount == 2)
            {
                PipPlayer.TogglePlayPause();
                e.Handled = true;
                return;
            }

            _mouseDownPos = e.GetPosition(this);
            _potentialDrag = true;
            CaptureMouse();
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_potentialDrag || e.LeftButton != MouseButtonState.Pressed) return;

            var delta = e.GetPosition(this) - _mouseDownPos;
            if (delta.Length > DragThreshold)
            {
                // Transition from "potential drag" to "actual drag":
                // release capture first (DragMove calls SetCapture internally)
                _potentialDrag = false;
                ReleaseMouseCapture();
                DragMove(); // blocks until the user releases the mouse button
            }
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_potentialDrag)
            {
                // Mouse was released without crossing the drag threshold — no action
                _potentialDrag = false;
                ReleaseMouseCapture();
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  PipPlayer Events
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// User pressed the "exit PiP" button inside the overlay.
        /// Capture the current PiP state and hand it back to MainPlayer so
        /// PlayerNormal resumes seamlessly — IMPORTANT (2).
        /// </summary>
        private void PipPlayer_ExitModeRequested(object? sender, EventArgs e)
        {
            var snap = PipPlayer.CaptureState();
            var main = new MainPlayer(_course, _db, snap);
            Close();
            _owner.Close();
            main.Show();
            // Close without triggering the fallback path in MainPlayer.PipWindow_Closed
            // (ReturnFromPip already cleared _pipWindow on the main player side)
        }

        /// <summary>
        /// Video finished playing inside PiP.  Return state to Normal so the
        /// user can interact normally (e.g. navigate to the next video).
        /// </summary>
        private void PipPlayer_PlaybackEnded(object? sender, EventArgs e)
        {
            var snap = PipPlayer.CaptureState();
            var main = new MainPlayer(_course, _db, snap);
            Close();
            _owner.Close();
            main.Show();
        }

        /// <summary>
        /// Prev/Next navigation inside PiP is not supported —
        /// the PiP window intentionally has no course reference.
        /// The user should exit PiP first, then navigate.
        /// </summary>
        private void PipPlayer_PreviousRequested(object? sender, EventArgs e) {
            if (_currentVideo == null) return;
            var prev = _course.GetPreviousVideo(_currentVideo);
            if (prev != null)
            { 
                _ = LoadVideoWithResumeAsync(prev);
            }
        }
        private void PipPlayer_NextRequested(object? sender, EventArgs e) {
            if (_currentVideo == null) return;
            var next = _course.GetNextVideo(_currentVideo);
            if (next != null)
            {
                _ = LoadVideoWithResumeAsync(next);
            }
        }

        private async Task LoadVideoWithResumeAsync(VideoModel video)
        {
            _currentVideo = video;

            // Inherit current volume / speed from whichever player is active so the
            // transition feels seamless when switching videos.
            var current = PipPlayer.CaptureState();

            var snap = new PlayerSnapshot
            {
                Video = video,
                StartPos = TimeSpan.Zero,
                Volume = current.Video != null ? current.Volume : 1.0,
                Speed = current.Video != null ? current.Speed : 1.0,
                State = PlayerState.Playing,
                Timecodes = video.Timestamps.Cast<ITimecode>().ToList()
            };

            PipPlayer.LoadState(snap);

            await Task.CompletedTask; // keeps the method async for future awaits
        }

    }
}