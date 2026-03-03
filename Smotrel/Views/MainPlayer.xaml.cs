// ════════════════════════════════════════════════════════════════════════════════
//  MainPlayer.xaml.cs  —  оркестратор режимов Normal / Fullscreen / PiP
// ════════════════════════════════════════════════════════════════════════════════
//
//  АРХИТЕКТУРА ПЛЕЕРОВ
//  ───────────────────
//
//  MainPlayer управляет тремя экземплярами SmotrelPlayer:
//
//    PlayerNormal     — всегда в дереве XAML (NormalLayout)
//    PlayerFullscreen — всегда в дереве XAML (FullscreenLayout), Collapsed когда не нужен
//    _pipPlayer       — создаётся/уничтожается вместе с PipPlayerWindow
//
//  _activePlayer — вычисляемое свойство, указывает на плеер текущего режима.
//  Все команды (LoadVideo, SeekTo, горячие клавиши) идут через _activePlayer.
//
//  ИНВАРИАНТЫ (всегда выполняются):
//    • Ровно один плеер НЕ заблокирован (IsLocked=false) — это _activePlayer
//    • Остальные плееры заблокированы (IsLocked=true) и на паузе
//    • PlayerState "активного" плеера отражает намерение пользователя
//
// ════════════════════════════════════════════════════════════════════════════════
//
//  ПЕРЕХОД МЕЖДУ РЕЖИМАМИ — общая схема
//  ─────────────────────────────────────
//
//  Каждый SwitchTo* следует этому порядку:
//
//    1. СНЭПШОТ — читаем из _activePlayer: position, isPlaying, volume
//       (делаем это ПЕРВЫМ, до любых изменений состояния)
//
//    2. ЗАМОРОЗКА старого плеера:
//         IsLocked = true    →  Media.Pause() напрямую, таймер стоп
//         PlayerState = Paused  (чтобы при разблокировке не стартовал сам)
//
//    3. ПОДГОТОВКА нового плеера:
//         LoadVideo(currentVideo, timecodes, position)  — только источник
//         Volume = снэпшот.volume
//         PlayerState = снэпшот.isPlaying ? Playing : Paused
//              → OnPlayerStateChanged сохраняет намерение
//         IsLocked = false
//              → OnIsLockedChanged: если _isMediaReady && PlayerState==Playing → Play()
//              → если ещё не готов → MediaOpened применит PlayerState
//
//    4. ПЕРЕКЛЮЧЕНИЕ Layout / Window
//
// ════════════════════════════════════════════════════════════════════════════════
//
//  СОБЫТИЯ от SmotrelPlayer (все Bubble)
//  ──────────────────────────────────────
//
//    PlaybackStateChanged    — поднимается при изменении PlayerState (Play/Pause)
//                              MainPlayer: обновляет Nav позицию
//                              НЕ поднимается во время скраббинга (см. SmotrelPlayer)
//
//    PlaybackEnded           — поднимается в Media_MediaEnded (после смены PlayerState→Paused)
//                              MainPlayer: загружает следующее видео или блокирует плеер
//
//    PreviousRequested       — кнопка "Назад" или Shift+Left
//    NextRequested           — кнопка "Вперёд" или Shift+Right
//                              MainPlayer: LoadVideoIntoActive(prev/next)
//
//    VideoWindowStateChanged — кнопки Fullscreen/PiP/ExitMode или клавиши F/P/Esc
//                              MainPlayer: SwitchTo*(...)
//
//    VolumeChangedRouted     — изменение громкости (пока не используется для синхронизации)
//
// ════════════════════════════════════════════════════════════════════════════════

using Smotrel.Controls;
using Smotrel.Enums;
using Smotrel.Interfaces;
using Smotrel.Models;
using Smotrel.Services;
using Smotrel.Settings;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Smotrel.Views
{
    public partial class MainPlayer : Window
    {
        // ── Данные курса ──────────────────────────────────────────────────────

        public CourseModel Course { get; }

        private VideoModel? _currentVideo = null;
        private IList<ITimecode> _currentTimecodes = [];

        // ── Режим и активный плеер ────────────────────────────────────────────

        private PlayerMode _currentMode = PlayerMode.Normal;

        /// <summary>
        /// Возвращает активный плеер для текущего режима.
        /// Гарантировано не null — при PiP без _pipPlayer падбэк на PlayerNormal.
        /// </summary>
        private SmotrelPlayer ActivePlayer => _currentMode switch
        {
            PlayerMode.Fullscreen => PlayerFullscreen,
            PlayerMode.PiP => _pipPlayer ?? PlayerNormal,
            _ => PlayerNormal,
        };

        // ── PiP ──────────────────────────────────────────────────────────────

        private PipPlayerWindow? _pipWindow;
        private SmotrelPlayer? _pipPlayer;

        // ── Сервисы ───────────────────────────────────────────────────────────

        private readonly DispatcherTimer _navUpdateTimer = new()
        { Interval = TimeSpan.FromMilliseconds(500) };

        private LastPositionService? _positionSaver;

        // ── Конструктор ───────────────────────────────────────────────────────

        public MainPlayer(CourseModel course)
        {
            InitializeComponent();

            Course = course;
            GapTitle.Text = "Smotrel — " + Course.Label;

            Loaded += OnWindowStateChanged;
            Loaded += OnLoaded;

            _navUpdateTimer.Tick += (_, _) => Nav.UpdatePosition(ActivePlayer.CurrentTime);
            _navUpdateTimer.Start();
        }

        // ── Инициализация ─────────────────────────────────────────────────────

        private void OnLoaded(object sender, EventArgs e)
        {
            var firstVideo = Course.GetVideoByAbsoluteIndex(0);
            _currentVideo = FindResumeVideo(Course.MainChapter) ?? firstVideo;

            Nav.Initialize(Course, MainWindow.Context);

            if (_currentVideo != null)
                LoadVideoIntoActive(_currentVideo, restorePosition: true);

            _positionSaver = new LastPositionService(MainWindow.Context);
            _positionSaver.Start(() => (_currentVideo, ActivePlayer.CurrentTime));
        }

        protected override async void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (_positionSaver != null)
                await _positionSaver.FlushAndStop();
        }

        // ── Поиск видео для продолжения ───────────────────────────────────────

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

        // ════════════════════════════════════════════════════════════════════════
        //  ЗАГРУЗКА ВИДЕО
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Загружает видео в активный плеер.
        /// Не меняет режим окна — используй SwitchTo* для этого.
        /// </summary>
        private void LoadVideoIntoActive(VideoModel video, bool restorePosition = false)
        {
            _currentVideo = video;
            _currentTimecodes = BuildTimecodes(video);

            var startPos = restorePosition && video.LastPosition > TimeSpan.Zero
                ? video.LastPosition
                : TimeSpan.Zero;

            // PlayerState у ActivePlayer уже содержит намерение (Playing/Paused).
            // LoadVideo только устанавливает источник; воспроизведение запустит MediaOpened.
            ActivePlayer.LoadVideo(video, _currentTimecodes, startPos);
            Nav?.SetCurrentVideo(video);
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
        // ════════════════════════════════════════════════════════════════════════

        // ── Снэпшот состояния ─────────────────────────────────────────────────

        /// <summary>
        /// Снимает снэпшот с активного плеера перед переключением режима.
        /// Вызывается ПЕРВЫМ делом в каждом SwitchTo*, до любых изменений.
        /// </summary>
        private (TimeSpan position, bool isPlaying, double volume) SnapshotActivePlayer()
            => (
                position: ActivePlayer.CurrentTime,
                isPlaying: ActivePlayer.PlayerState == PlayerState.Playing,
                volume: ActivePlayer.Volume
            );

        /// <summary>
        /// Замораживает плеер: останавливает воспроизведение и блокирует UI.
        /// Вызывается для "донора" при каждом переключении режима.
        /// </summary>
        private static void FreezePlayer(SmotrelPlayer player, string? lockMessage = null)
        {
            // Сначала меняем PlayerState — это остановит таймер и Media корректно
            player.PlayerState = PlayerState.Paused;
            player.IsLocked = true;

            if (lockMessage != null)
                player.LockMessage = lockMessage;
        }

        /// <summary>
        /// Применяет снэпшот к плееру-получателю: загружает видео, восстанавливает позицию и Volume.
        /// PlayerState задаётся снаружи (после этого метода) чтобы сохранить явность потока.
        /// </summary>
        private void ApplySnapshotToPlayer(SmotrelPlayer player,
            TimeSpan position, double volume)
        {
            if (_currentVideo == null) return;

            player.Volume = volume;

            // Разблокируем ДО LoadVideo — иначе MediaOpened не запустит Play даже если PlayerState==Playing
            player.IsLocked = false;

            player.LoadVideo(_currentVideo, _currentTimecodes, position);
        }

        // ── Normal ────────────────────────────────────────────────────────────

        /// <summary>
        /// Переход в Normal режим из Fullscreen или PiP.
        ///
        /// Последовательность событий после вызова:
        ///   1. FreezePlayer(_activePlayer) → PlayerState=Paused, IsLocked=true, Media.Pause()
        ///   2. _currentMode = Normal
        ///   3. ApplySnapshotToPlayer(PlayerNormal) → IsLocked=false, LoadVideo
        ///   4. PlayerNormal.PlayerState = Paused/Playing (из снэпшота)
        ///      → если _isMediaReady: Media.Play/Pause сразу
        ///      → если не готов: MediaOpened применит состояние
        ///   5. Layout переключается
        /// </summary>
        private void SwitchToNormal()
        {
            if (_currentMode == PlayerMode.Normal) return;

            // 1. Снэпшот — читаем ДО любых изменений
            var (position, isPlaying, volume) = SnapshotActivePlayer();
            var prevMode = _currentMode;

            // 2. Заморозка источника
            FreezePlayer(ActivePlayer);

            // 3. Закрываем PiP-окно (если был PiP)
            if (prevMode == PlayerMode.PiP && _pipWindow != null)
            {
                UnsubscribePipPlayer();
                _pipWindow.ForceClose();
                _pipWindow = null;
                _pipPlayer = null;
            }

            // 4. Скрываем Fullscreen layout (если был Fullscreen)
            if (prevMode == PlayerMode.Fullscreen)
            {
                FullscreenLayout.Visibility = Visibility.Collapsed;
                NormalLayout.Visibility = Visibility.Visible;
            }

            // 5. Переключаем режим
            _currentMode = PlayerMode.Normal;

            // 6. Готовим PlayerNormal
            PlayerNormal.LockMessage = "Контент недоступен"; // сброс сообщения
            PlayerNormal.PlayerMode = PlayerMode.Normal;
            PlayerNormal.PlayerState = isPlaying ? PlayerState.Playing : PlayerState.Paused;

            ApplySnapshotToPlayer(PlayerNormal, position, volume);
            // ApplySnapshotToPlayer разблокирует PlayerNormal и вызовет LoadVideo.
            // MediaOpened запустит/остановит воспроизведение согласно PlayerState.
        }

        // ── Fullscreen ────────────────────────────────────────────────────────

        /// <summary>
        /// Переход в Fullscreen режим.
        ///
        /// Последовательность событий:
        ///   1. Снэпшот с PlayerNormal (или PiP если переход из PiP — не поддерживается напрямую)
        ///   2. FreezePlayer(PlayerNormal) → PlayerState=Paused, IsLocked=true
        ///   3. PlayerFullscreen: PlayerState=снэпшот, ApplySnapshot
        ///      → MediaOpened стартует воспроизведение
        ///   4. Layout: NormalLayout=Collapsed, FullscreenLayout=Visible
        /// </summary>
        private void SwitchToFullscreen()
        {
            if (_currentMode == PlayerMode.Fullscreen) return;

            // 1. Снэпшот
            var (position, isPlaying, volume) = SnapshotActivePlayer();

            // 2. Заморозка источника
            FreezePlayer(ActivePlayer);

            // 3. Переключаем режим
            _currentMode = PlayerMode.Fullscreen;

            // 4. Готовим PlayerFullscreen
            PlayerFullscreen.PlayerMode = PlayerMode.Fullscreen;
            PlayerFullscreen.PlayerState = isPlaying ? PlayerState.Playing : PlayerState.Paused;

            ApplySnapshotToPlayer(PlayerFullscreen, position, volume);

            // 5. Layout
            NormalLayout.Visibility = Visibility.Collapsed;
            FullscreenLayout.Visibility = Visibility.Visible;
        }

        // ── PiP ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Переход в PiP режим.
        ///
        /// Последовательность событий:
        ///   1. Снэпшот с PlayerNormal
        ///   2. FreezePlayer(PlayerNormal) + сообщение о PiP
        ///   3. Создаём PipPlayerWindow + _pipPlayer
        ///   4. Подписываемся на события _pipPlayer
        ///   5. _pipPlayer: PlayerState=снэпшот, ApplySnapshot
        ///      → MediaOpened стартует воспроизведение
        ///   6. pipWindow.Show()
        /// </summary>
        private void SwitchToPiP()
        {
            if (_currentMode == PlayerMode.PiP) return;

            // 1. Снэпшот
            var (position, isPlaying, volume) = SnapshotActivePlayer();

            // 2. Заморозка PlayerNormal
            FreezePlayer(PlayerNormal, lockMessage: "Воспроизводится\nв Picture-in-Picture");

            // 3. Создаём PiP окно
            _pipWindow = new PipPlayerWindow(this);
            _pipPlayer = _pipWindow.Player;

            // 4. Подписка на события
            SubscribePipPlayer();

            // 5. Переключаем режим
            _currentMode = PlayerMode.PiP;

            // 6. Готовим _pipPlayer
            _pipPlayer.PlayerMode = PlayerMode.PiP;
            _pipPlayer.OverlayTimeout = PlayerNormal.OverlayTimeout;
            _pipPlayer.PlayerState = isPlaying ? PlayerState.Playing : PlayerState.Paused;

            ApplySnapshotToPlayer(_pipPlayer, position, volume);

            // 7. Показываем окно
            _pipWindow.Show();
        }

        // ── Управление подпиской PiP ──────────────────────────────────────────

        private void SubscribePipPlayer()
        {
            if (_pipPlayer == null) return;

            _pipPlayer.PlaybackStateChanged += Player_PlaybackStateChanged;
            _pipPlayer.PlaybackEnded += Player_PlaybackEnded;
            _pipPlayer.PreviousRequested += Player_PreviousRequested;
            _pipPlayer.NextRequested += Player_NextRequested;
            _pipPlayer.VolumeChangedRouted += Player_VolumeChanged;
            _pipPlayer.VideoWindowStateChanged += Player_WindowStateChanged;
        }

        private void UnsubscribePipPlayer()
        {
            if (_pipPlayer == null) return;

            _pipPlayer.PlaybackStateChanged -= Player_PlaybackStateChanged;
            _pipPlayer.PlaybackEnded -= Player_PlaybackEnded;
            _pipPlayer.PreviousRequested -= Player_PreviousRequested;
            _pipPlayer.NextRequested -= Player_NextRequested;
            _pipPlayer.VolumeChangedRouted -= Player_VolumeChanged;
            _pipPlayer.VideoWindowStateChanged -= Player_WindowStateChanged;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  СОБЫТИЯ SmotrelPlayer
        // ════════════════════════════════════════════════════════════════════════
        //
        //  PlayerNormal и PlayerFullscreen подписаны через XAML (Bubble-события).
        //  _pipPlayer подписывается/отписывается в SubscribePipPlayer/Unsubscribe.

        /// <summary>
        /// Вызывается при любом Play/Pause.
        /// НЕ вызывается во время скраббинга (см. SmotrelPlayer).
        /// </summary>
        private void Player_PlaybackStateChanged(object sender, RoutedEventArgs e)
        {
            Nav.UpdatePosition(ActivePlayer.CurrentTime);
        }

        /// <summary>
        /// Вызывается после Media_MediaEnded (PlayerState уже == Paused к этому моменту).
        /// Загружает следующее видео или завершает курс.
        /// </summary>
        private void Player_PlaybackEnded(object sender, RoutedEventArgs e)
        {
            if (_currentVideo == null) return;

            var next = Course.GetNextVideo(_currentVideo);
            if (next != null)
            {
                // LoadVideoIntoActive не меняет PlayerState — плеер начнёт с Paused
                // (из Media_MediaEnded). Хотим автоплей → выставляем явно.
                ActivePlayer.PlayerState = PlayerState.Playing;
                LoadVideoIntoActive(next);
            }
            else
            {
                ActivePlayer.IsLocked = true;
                ActivePlayer.LockMessage = "Курс завершён";
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

        private void Player_VolumeChanged(object sender, RoutedEventArgs e) { /* зарезервировано */ }

        private void Player_WindowStateChanged(object sender, RoutedEventArgs e)
        {
            if (e is not VideoWindowStateRequestEventArgs req) return;

            switch (req.Request)
            {
                case VideoWindowStateRequest.Normal:
                    SwitchToNormal();
                    break;
                case VideoWindowStateRequest.Fullscreen:
                    SwitchToFullscreen();
                    break;
                case VideoWindowStateRequest.PiP:
                    SwitchToPiP();
                    break;
            }
        }

        // ── Навигация ─────────────────────────────────────────────────────────

        private void Nav_VideoRequested(VideoModel video)
            => LoadVideoIntoActive(video);

        private void Nav_SeekRequested(TimeSpan pos)
            => ActivePlayer.SeekTo(pos);

        private void Nav_TimecodeChanged(VideoModel video)
        {
            if (_currentVideo?.Id == video.Id)
            {
                var timecodes = BuildTimecodes(video);
                ActivePlayer.Timeline.Timecodes = timecodes;
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  PiP: обратный вызов от PipPlayerWindow
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Вызывается PipPlayerWindow когда пользователь закрыл PiP вручную.
        /// Используем Dispatcher.BeginInvoke чтобы не вызывать SwitchToNormal
        /// в середине обработчика Closing.
        /// </summary>
        public void OnPipWindowClosedByUser()
        {
            Dispatcher.BeginInvoke(() => SwitchToNormal());
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ГОРЯЧИЕ КЛАВИШИ
        // ════════════════════════════════════════════════════════════════════════

        private void MainPlayer_KeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBox or TextBlock) return;

            if (ActivePlayer.HandleHotkey(e.Key, Keyboard.Modifiers))
                e.Handled = true;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  КНОПКИ ЗАГОЛОВКА
        // ════════════════════════════════════════════════════════════════════════

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
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
    }
}