using Smotrel.Controls;
using Smotrel.Enums;
using Smotrel.Interfaces;
using Smotrel.Models;
using Smotrel.Services;
using Smotrel.Settings;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Smotrel.Views
{
    /// <summary>
    /// Главное окно плеера.
    ///
    /// Содержит два SmotrelPlayer в одной сетке:
    ///   PlayerNormal     (Col 0, всегда в Layout)
    ///   PlayerFullscreen (FullscreenLayout Grid, только в Fullscreen)
    ///
    /// PiP — отдельное маленькое окно PipPlayerWindow с третьим PlayerPip.
    ///
    /// ActivePlayer — ссылка на тот плеер, который получает команды
    /// (LoadVideo, HandleHotkey и т.д.)
    ///
    /// При переключении режимов:
    ///   • Позиция, состояние Play/Pause, громкость — синхронизируются.
    ///   • Активный плеер меняется.
    ///   • Неактивный плеер блокируется (IsLocked=True).
    /// </summary>
    public partial class MainPlayer : Window
    {
        // ── Курс и текущее видео ──────────────────────────────────────────────

        public CourseModel Course { get; }
        private VideoModel? _currentVideo;
        private IList<ITimecode> _currentTimecodes = [];

        // ── Режим и активный плеер ────────────────────────────────────────────

        private PlayerMode _currentMode = PlayerMode.Normal;
        private SmotrelPlayer _activePlayer => _currentMode switch
        {
            PlayerMode.Fullscreen => PlayerFullscreen,
            PlayerMode.PiP => _pipPlayer ?? PlayerNormal,
            _ => PlayerNormal,
        };

        // ── PiP ──────────────────────────────────────────────────────────────

        private PipPlayerWindow? _pipWindow;
        private SmotrelPlayer? _pipPlayer;

        // ── Конструктор ───────────────────────────────────────────────────────

        public MainPlayer(CourseModel course)
        {
            InitializeComponent();

            Course = course;
            GapTitle.Text = "Smotrel — " + Course.Label;

            Loaded += OnWindowStateChanged;
            Loaded += OnLoaded;
        }

        // ── Инициализация ─────────────────────────────────────────────────────

        private void OnLoaded(object sender, EventArgs e)
        {
            _currentVideo = Course.GetVideoByAbsoluteIndex(0);
            if (_currentVideo == null) return;
            LoadVideoIntoActive(_currentVideo);
        }

        // ── Загрузка видео ────────────────────────────────────────────────────

        private void LoadVideoIntoActive(VideoModel video)
        {
            _currentVideo = video;
            _currentTimecodes = BuildTimecodes(video);
            _activePlayer.LoadVideo(video, _currentTimecodes);
        }

        private static IList<ITimecode> BuildTimecodes(VideoModel video)
        {
            if (video.Timestamps.Count > 0)
                return video.Timestamps.Cast<ITimecode>().ToList();
            if (video.Duration == TimeSpan.Zero)
                return [];

            return Enumerable.Range(1, 4)
                .Select(i => VideoTimecode.Create(
                    TimeSpan.FromSeconds(video.Duration.TotalSeconds * i * 0.2),
                    $"Часть {i}"))
                .Cast<ITimecode>()
                .ToList();
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ПЕРЕКЛЮЧЕНИЕ РЕЖИМОВ
        //  Логика вынесена полностью из SmotrelPlayer — SmotrelPlayer только
        //  уведомляет через VideoWindowStateChanged, MainPlayer решает что делать.
        // ════════════════════════════════════════════════════════════════════════

        private void SwitchToNormal()
        {
            if (_currentMode == PlayerMode.Normal) return;

            var prevMode = _currentMode;
            var position = _activePlayer.CurrentTime;
            var isPlaying = _activePlayer.PlayerState == PlayerState.Playing;
            var volume = _activePlayer.Volume;

            // Закрываем PiP-окно
            if (prevMode == PlayerMode.PiP && _pipWindow != null)
            {
                _pipWindow.ForceClose(); // не поднимет событие Normal снова
                _pipWindow = null;
                _pipPlayer = null;
            }

            // Убираем Fullscreen
            if (prevMode == PlayerMode.Fullscreen)
            {
                FullscreenLayout.Visibility = Visibility.Collapsed;
                NormalLayout.Visibility = Visibility.Visible;
                PlayerFullscreen.IsLocked = true;
                PlayerFullscreen.PlayerState = PlayerState.Paused;
            }

            // Разблокируем основной плеер, восстанавливаем позицию и состояние
            _currentMode = PlayerMode.Normal;

            PlayerNormal.PlayerMode = PlayerMode.Normal;
            PlayerNormal.IsLocked = false;
            PlayerNormal.Volume = volume;
            PlayerNormal.SeekTo(position);

            if (isPlaying)
                PlayerNormal.PlayerState = PlayerState.Playing;
        }

        private void SwitchToFullscreen()
        {
            if (_currentMode == PlayerMode.Fullscreen) return;

            var position = _activePlayer.CurrentTime;
            var isPlaying = _activePlayer.PlayerState == PlayerState.Playing;
            var volume = _activePlayer.Volume;

            // Загружаем видео в fullscreen-плеер
            if (_currentVideo != null)
                PlayerFullscreen.LoadVideo(_currentVideo, _currentTimecodes);

            PlayerFullscreen.Volume = volume;
            PlayerFullscreen.SeekTo(position);
            PlayerFullscreen.PlayerMode = PlayerMode.Fullscreen;
            PlayerFullscreen.IsLocked = false;

            if (isPlaying)
                PlayerFullscreen.PlayerState = PlayerState.Playing;
            else
                PlayerFullscreen.PlayerState = PlayerState.Paused;

            // Блокируем Normal
            PlayerNormal.PlayerState = PlayerState.Paused;
            PlayerNormal.IsLocked = true;

            NormalLayout.Visibility = Visibility.Collapsed;
            FullscreenLayout.Visibility = Visibility.Visible;

            _currentMode = PlayerMode.Fullscreen;
        }

        private void SwitchToPiP()
        {
            if (_currentMode == PlayerMode.PiP) return;

            var position = _activePlayer.CurrentTime;
            var isPlaying = _activePlayer.PlayerState == PlayerState.Playing;
            var volume = _activePlayer.Volume;

            // Блокируем текущий активный плеер с PiP-сообщением
            PlayerNormal.IsLocked = true;
            PlayerNormal.LockMessage = "Воспроизводится\nв Picture-in-Picture";
            PlayerNormal.PlayerState = PlayerState.Paused;

            // Создаём PiP-окно
            _pipWindow = new PipPlayerWindow(this);
            _pipPlayer = _pipWindow.Player;

            _pipPlayer.PlayerMode = PlayerMode.PiP;
            _pipPlayer.OverlayTimeout = PlayerNormal.OverlayTimeout;

            // Подписываемся на события pip-плеера
            _pipPlayer.PlaybackStateChanged += Player_PlaybackStateChanged;
            _pipPlayer.PlaybackEnded += Player_PlaybackEnded;
            _pipPlayer.PreviousRequested += Player_PreviousRequested;
            _pipPlayer.NextRequested += Player_NextRequested;
            _pipPlayer.VolumeChangedRouted += Player_VolumeChanged;
            _pipPlayer.VideoWindowStateChanged += Player_WindowStateChanged;

            if (_currentVideo != null)
                _pipPlayer.LoadVideo(_currentVideo, _currentTimecodes);

            _pipPlayer.Volume = volume;
            _pipPlayer.SeekTo(position);

            if (isPlaying)
                _pipPlayer.PlayerState = PlayerState.Playing;
            else
                _pipPlayer.PlayerState = PlayerState.Paused;

            _pipWindow.Show();
            _currentMode = PlayerMode.PiP;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  СОБЫТИЯ SmotrelPlayer (Bubble — приходят от любого из трёх плееров)
        // ════════════════════════════════════════════════════════════════════════

        private void Player_PlaybackStateChanged(object sender, RoutedEventArgs e) { }

        private void Player_PlaybackEnded(object sender, RoutedEventArgs e)
        {
            if (_currentVideo == null) return;
            var next = Course.GetNextVideo(_currentVideo);
            if (next != null)
                LoadVideoIntoActive(next);
            else
            {
                _activePlayer.IsLocked = true;
                _activePlayer.LockMessage = "Курс завершён";
            }
        }

        private void Player_PreviousRequested(object sender, RoutedEventArgs e)
        {
            if (_currentVideo == null) return;
            var prev = Course.GetPreviousVideo(_currentVideo);
            if (prev != null) LoadVideoIntoActive(prev);
        }

        private void Player_NextRequested(object sender, RoutedEventArgs e)
        {
            if (_currentVideo == null) return;
            var next = Course.GetNextVideo(_currentVideo);
            if (next != null) LoadVideoIntoActive(next);
        }

        private void Player_VolumeChanged(object sender, RoutedEventArgs e) { }

        private void Player_WindowStateChanged(object sender, RoutedEventArgs e)
        {
            if (e is VideoWindowStateRequestEventArgs req)
            {
                switch (req.Request)
                {
                    case VideoWindowStateRequest.Normal: SwitchToNormal(); break;
                    case VideoWindowStateRequest.Fullscreen: SwitchToFullscreen(); break;
                    case VideoWindowStateRequest.PiP: SwitchToPiP(); break;
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ГОРЯЧИЕ КЛАВИШИ
        // ════════════════════════════════════════════════════════════════════════

        private void MainPlayer_KeyDown(object sender, KeyEventArgs e)
        {
            // Защита: обрабатываем только если плеер в фокусе или это наше окно
            // (не TextBox или другой input-элемент)
            if (Keyboard.FocusedElement is TextBox or TextBlock) return;

            if (_activePlayer.HandleHotkey(e.Key, Keyboard.Modifiers))
                e.Handled = true;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  КНОПКИ ЗАГОЛОВКА
        // ════════════════════════════════════════════════════════════════════════

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            // При необходимости закрываем PiP
            _pipWindow?.ForceClose();
            new MainWindow().Show();
            Close();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal : WindowState.Maximized;

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _pipWindow?.ForceClose();
            Close();
            Application.Current.Shutdown();
        }

        private void Header_Down(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        // ── Maximized / Normal подстройка ─────────────────────────────────────

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
                if (borderExit != null)
                {
                    borderExit.CornerRadius = new CornerRadius(0);
                    borderExit.Width = 40;
                }
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
        /// <summary>
        /// Вызывается PipPlayerWindow когда пользователь закрыл PiP сам (не через ExitMode).
        /// </summary>
        public void OnPipWindowClosedByUser()
        {
            SwitchToNormal();
        }

    }
}

// ──────────────────────────────────────────────────────────────────────────
// Метод вызывается PipPlayerWindow когда пользователь закрывает PiP сам
// (нажал крестик или другой системный способ закрытия).
// ADDITION