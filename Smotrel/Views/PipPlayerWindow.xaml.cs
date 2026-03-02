using Smotrel.Controls;
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
        private readonly MainPlayer _owner;

        /// <summary>Флаг: закрытие инициировано MainPlayer, а не пользователем.</summary>
        private bool _forceClosing;

        public PipPlayerWindow(MainPlayer owner)
        {
            InitializeComponent();
            _owner = owner;
        }

        // ── Перетаскивание ────────────────────────────────────────────────────

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Одиночный клик — DragMove только если не по кнопке
            if (e.OriginalSource is System.Windows.Controls.Button) return;
            if (e.OriginalSource is System.Windows.Controls.TextBlock tb
                && tb.Parent is System.Windows.Controls.Button)       return;

            try { DragMove(); } catch { }
        }

        // ── Закрытие ──────────────────────────────────────────────────────────

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);

            if (!_forceClosing)
            {
                // Пользователь закрыл PiP вручную → возвращаем в Normal
                // Не отменяем — пусть окно закроется, MainPlayer восстановит Normal
                ActivateMainPlayer();
            }
        }

        /// <summary>Активирует MainPlayer: Topmost → Activate → снимаем Topmost.</summary>
        private void ActivateMainPlayer()
        {
            _owner.Topmost = true;
            _owner.Activate();
            _owner.Topmost = false;
        }

        /// <summary>Закрытие из кода MainPlayer (без повторного вызова SwitchToNormal).</summary>
        public void ForceClose()
        {
            _forceClosing = true;
            ActivateMainPlayer();
            Close();
        }

        private void PipWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Если закрывает пользователь (не ForceClose) — просим MainPlayer вернуться в Normal
            if (!_forceClosing)
            {
                // Используем Dispatcher чтобы не вызывать SwitchToNormal из середины Closing
                Dispatcher.BeginInvoke(() =>
                {
                    // Вызываем через reflection-like доступ к публичному методу
                    // MainPlayer подпишется на это через собственный механизм
                    // или можно сделать публичный метод:
                    _owner.OnPipWindowClosedByUser();
                });
            }
        }
    }
}
