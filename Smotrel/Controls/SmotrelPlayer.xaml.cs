// ════════════════════════════════════════════════════════════════════════════════
//  SmotrelPlayer.xaml.cs  —  рефакторинг состояния плеера
// ════════════════════════════════════════════════════════════════════════════════
//
//  ПРОБЛЕМЫ ОРИГИНАЛА (все исправлены):
//
//  1. _positionTimer.Stop() в Media_MediaEnded не перезапускался при следующем
//     LoadVideo, потому что PlayerState не менялся → время зависало навсегда.
//
//  2. Media_MediaEnded не обновлял PlayerState → кнопка Play/Pause и иконка
//     оставались в состоянии "играет", хотя MediaElement уже остановился.
//
//  3. OnPlayerStateChanged вызывал Media.Play() до того как MediaOpened сработал
//     (источник ещё не загружен) — при этом если media.NaturalDuration.HasTimeSpan
//     == false, таймер тикал впустую, потом дополнительно зависал.
//
//  4. Timeline_SeekStarted менял PlayerState → поднимал PlaybackStateChangedEvent
//     в MainPlayer → лишние обращения к Nav.UpdatePosition во время скраббинга.
//
//  5. OnFilePathChanged дублировал логику LoadVideo и вёл себя несовместимо с ней.
//
// ════════════════════════════════════════════════════════════════════════════════
//
//  ЖИЗНЕННЫЙ ЦИКЛ MediaElement
//  ───────────────────────────
//
//    LoadVideo(video, timecodes, startPos)
//      │  Устанавливает Media.Source = new Uri(...)
//      │  _isMediaReady = false  →  таймер позиции ОСТАНОВЛЕН
//      │  Сохраняет _pendingSeekPos / _hasPendingSeek
//      │  НЕ вызывает Media.Play/Pause — только хранит намерение (_playIntent)
//      │  НЕ меняет PlayerState
//      ▼
//    ─────── WPF загружает файл ───────
//      ▼
//    Media_MediaOpened  (единственное место, откуда стартует воспроизведение)
//      │  _isMediaReady = true
//      │  Timeline.Duration = реальная длительность
//      │  Применяет отложенный seek (_pendingSeekPos)
//      │  Применяет Volume / SpeedRatio
//      │  Если PlayerState == Playing  →  Media.Play() + _positionTimer.Start()
//      │  Если PlayerState == Paused   →  Media.Pause()
//      ▼
//    [воспроизведение идёт, _positionTimer тикает]
//      ▼
//    Media_MediaEnded  ─ или ─  Media_MediaFailed
//      │  _isMediaReady = false
//      │  _positionTimer.Stop()
//      │  PlayerState = Paused          ← синхронизирует UI (кнопка, глиф)
//      │  Ended: RaiseEvent(PlaybackEndedEvent) → MainPlayer грузит следующее
//      ▼
//    [ждёт следующего LoadVideo]
//
//  ПЕРЕХОДЫ PlayerState
//  ────────────────────
//
//    OnPlayerStateChanged(Playing)
//      │  Если _isMediaReady  →  Media.Play() + _positionTimer.Start()
//      │  Если НЕ ready       →  ничего (MediaOpened применит состояние сам)
//      │  Обновляет иконку, запускает анимацию, поднимает PlaybackStateChangedEvent
//      ▼
//    OnPlayerStateChanged(Paused)
//      │  Media.Pause()  (безопасно вызывать даже если !_isMediaReady)
//      │  _positionTimer.Stop()
//      │  Обновляет иконку, поднимает PlaybackStateChangedEvent
//      ▼
//
//  СКРАББИНГ (перетаскивание ползунка)
//  ────────────────────────────────────
//
//    Timeline.SeekStarted
//      →  _isScrubbing = true
//      →  Media.Pause() напрямую (БЕЗ изменения PlayerState)
//      →  Глушим звук (Media.Volume = 0)
//      →  PlayerState НЕ МЕНЯЕТСЯ → PlaybackStateChangedEvent НЕ поднимается
//
//    Timeline.SeekCompleted
//      →  Media.Position = финальная позиция
//      →  Восстанавливаем Volume
//      →  Если PlayerState == Playing  →  Media.Play() + таймер
//      →  _isScrubbing = false
//
// ════════════════════════════════════════════════════════════════════════════════

using Smotrel.Enums;
using Smotrel.Events;
using Smotrel.Interfaces;
using Smotrel.Models;
using Smotrel.Settings;
using System.Collections.Generic;
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
        // ── Внутреннее состояние ──────────────────────────────────────────────

        private readonly DispatcherTimer _overlayTimer = new();
        private readonly DispatcherTimer _positionTimer = new();

        private IList<ITimecode> _timecodes = new List<ITimecode>();

        // _isMediaReady: true только между MediaOpened и следующим LoadVideo/MediaFailed/MediaEnded.
        // Определяет: можно ли сейчас вызывать Media.Play/Seek и читать NaturalDuration.
        private bool _isMediaReady = false;

        // _isScrubbing: пользователь тащит ползунок.
        // Во время скраббинга: позиционный таймер пропускает тики,
        // Media.Pause() вызван напрямую (не через PlayerState).
        private bool _isScrubbing = false;
        private double _volumeBeforeScrub = 1.0;

        // Громкость до mute (для восстановления при снятии mute)
        private double _volumeBeforeMute = 1.0;

        // Скорость воспроизведения (хранится отдельно, применяется в MediaOpened)
        private double _playbackSpeed = 1.0;

        // Отложенная позиция: применяется в MediaOpened после загрузки нового файла
        private TimeSpan _pendingSeekPos = TimeSpan.Zero;
        private bool _hasPendingSeek = false;

        // ── Конструктор ───────────────────────────────────────────────────────

        public SmotrelPlayer()
        {
            InitializeComponent();

            _overlayTimer.Tick += OverlayTimer_Tick;
            _overlayTimer.Interval = OverlayTimeout;

            _positionTimer.Interval = TimeSpan.FromMilliseconds(250);
            _positionTimer.Tick += PositionTimer_Tick;

            // SeekStarted/SeekCompleted используют прямые вызовы Media без изменения PlayerState
            Timeline.SeekStarted += Timeline_SeekStarted;
            Timeline.SeekCompleted += Timeline_SeekCompleted;

            MouseMove += OnMouseActivity;
            MouseDown += OnMouseActivity;

            VolumeBar.Value = Volume;
            UpdateVolumeGlyph();

            // Таймер НЕ запускается здесь — только после MediaOpened
            Loaded += (_, _) => { ShowOverlay(); Focus(); };
        }

        // ════════════════════════════════════════════════════════════════════════
        //  DEPENDENCY PROPERTIES
        // ════════════════════════════════════════════════════════════════════════

        // ── FilePath ─────────────────────────────────────────────────────────
        //  Устаревший путь загрузки. Используйте LoadVideo().
        //  Оставлен для обратной совместимости с XAML-биндингами.
        //  НЕ вызывает Media.Play и не меняет PlayerState напрямую.

        public static readonly DependencyProperty FilePathProperty =
            DependencyProperty.Register(nameof(FilePath), typeof(string),
                typeof(SmotrelPlayer), new PropertyMetadata(null, OnFilePathChanged));

        public string? FilePath
        {
            get => (string?)GetValue(FilePathProperty);
            set => SetValue(FilePathProperty, value);
        }

        private static void OnFilePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // Только устанавливает источник; воспроизведение начинает Media_MediaOpened.
            var p = (SmotrelPlayer)d;
            if (e.NewValue is string path && !string.IsNullOrWhiteSpace(path))
                p.StartLoadingSource(new Uri(path), TimeSpan.Zero);
            else
            {
                p._isMediaReady = false;
                p._positionTimer.Stop();
                p.Media.Source = null;
            }
        }

        // ── Title ────────────────────────────────────────────────────────────

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string),
                typeof(SmotrelPlayer), new PropertyMetadata(string.Empty, OnTitleChanged));

        public new string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((SmotrelPlayer)d).TbTitle.Text = e.NewValue as string ?? string.Empty;

        // ── CurrentTime ──────────────────────────────────────────────────────
        //  Только для чтения снаружи (обновляется PositionTimer_Tick).
        //  Внешняя запись игнорируется если _isScrubbing == true.

        public static readonly DependencyProperty CurrentTimeProperty =
            DependencyProperty.Register(nameof(CurrentTime), typeof(TimeSpan),
                typeof(SmotrelPlayer), new PropertyMetadata(TimeSpan.Zero, OnCurrentTimeChanged));

        public TimeSpan CurrentTime
        {
            get => (TimeSpan)GetValue(CurrentTimeProperty);
            set => SetValue(CurrentTimeProperty, value);
        }

        private static void OnCurrentTimeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var p = (SmotrelPlayer)d;
            var pos = (TimeSpan)e.NewValue;

            // Внешний SeekTo → двигаем MediaElement (только если готов и не скробим)
            if (!p._isScrubbing && p._isMediaReady)
                p.Media.Position = pos;

            p.UpdateTimeLabel(pos);
            p.UpdateChapterLabel(pos);
            p.UpdateTimelineValue(pos);
        }

        // ── Volume ───────────────────────────────────────────────────────────

        public static readonly DependencyProperty VolumeProperty =
            DependencyProperty.Register(nameof(Volume), typeof(double),
                typeof(SmotrelPlayer),
                new PropertyMetadata(1.0, OnVolumeChanged),
                v => (double)v is >= 0.0 and <= 1.0);

        public double Volume
        {
            get => (double)GetValue(VolumeProperty);
            set => SetValue(VolumeProperty, value);
        }

        private static void OnVolumeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var p = (SmotrelPlayer)d;
            p.Media.Volume = (double)e.NewValue;
            p.VolumeBar.Value = (double)e.NewValue;
            p.UpdateVolumeGlyph();
        }

        // ── IsMuted ──────────────────────────────────────────────────────────

        public static readonly DependencyProperty IsMutedProperty =
            DependencyProperty.Register(nameof(IsMuted), typeof(bool),
                typeof(SmotrelPlayer), new PropertyMetadata(false, OnIsMutedChanged));

        public bool IsMuted
        {
            get => (bool)GetValue(IsMutedProperty);
            set => SetValue(IsMutedProperty, value);
        }

        private static void OnIsMutedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var p = (SmotrelPlayer)d;
            bool on = (bool)e.NewValue;

            if (on)
            {
                p._volumeBeforeMute = p.Volume;
                p.Media.Volume = 0;
                p.VolumeBar.Value = 0;
            }
            else
            {
                p.Volume = p._volumeBeforeMute;
                p.Media.Volume = p._volumeBeforeMute;
                p.VolumeBar.Value = p._volumeBeforeMute;
            }

            p.UpdateVolumeGlyph();
        }

        // ── PlayerState ──────────────────────────────────────────────────────
        //
        //  Единственный источник истины об «намерении» воспроизведения.
        //
        //  Важно: PlayerState НЕ гарантирует что MediaElement прямо сейчас играет —
        //  он может быть Playing пока _isMediaReady == false (медиа ещё грузится).
        //  Реальный запуск Media.Play() происходит только в OnPlayerStateChanged
        //  (если _isMediaReady) или в Media_MediaOpened (применяет сохранённое намерение).

        public static readonly DependencyProperty PlayerStateProperty =
            DependencyProperty.Register(nameof(PlayerState), typeof(PlayerState),
                typeof(SmotrelPlayer),
                new PropertyMetadata(PlayerState.Playing, OnPlayerStateChanged));

        public PlayerState PlayerState
        {
            get => (PlayerState)GetValue(PlayerStateProperty);
            set => SetValue(PlayerStateProperty, value);
        }

        private static void OnPlayerStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var p = (SmotrelPlayer)d;
            var oldState = (PlayerState)e.OldValue;
            var newState = (PlayerState)e.NewValue;

            if (newState == PlayerState.Playing)
            {
                // Реальный вызов Media.Play() только если медиа уже открыта.
                // Иначе: MediaOpened сам вызовет Play, увидев PlayerState == Playing.
                if (p._isMediaReady)
                {
                    p.Media.Play();
                    p._positionTimer.Start();
                }
                p.GlyphPlayPause.Text = "\uE769"; // Pause icon (показываем пока играет)
            }
            else // Paused
            {
                // Pause можно вызывать всегда — MediaElement игнорирует, если не готов
                p.Media.Pause();
                p._positionTimer.Stop();
                p.GlyphPlayPause.Text = "\uE768"; // Play icon
            }

            p.RunPlayStateAnimation(newState);
            p.RaiseEvent(new PlayerStateChangedEventArgs(PlaybackStateChangedEvent, oldState, newState));
        }

        // ── PlayerMode ───────────────────────────────────────────────────────

        public static readonly DependencyProperty PlayerModeProperty =
            DependencyProperty.Register(nameof(PlayerMode), typeof(PlayerMode),
                typeof(SmotrelPlayer),
                new PropertyMetadata(PlayerMode.Normal, OnPlayerModeChanged));

        public PlayerMode PlayerMode
        {
            get => (PlayerMode)GetValue(PlayerModeProperty);
            set => SetValue(PlayerModeProperty, value);
        }

        private static void OnPlayerModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var p = (SmotrelPlayer)d;
            var mode = (PlayerMode)e.NewValue;

            switch (mode)
            {
                case PlayerMode.Normal:
                    p.BtnFullscreen.Visibility = Visibility.Visible;
                    p.BtnPip.Visibility = Visibility.Visible;
                    p.BtnExitMode.Visibility = Visibility.Collapsed;
                    break;

                case PlayerMode.Fullscreen:
                    p.BtnFullscreen.Visibility = Visibility.Collapsed;
                    p.BtnPip.Visibility = Visibility.Collapsed;
                    p.BtnExitMode.Visibility = Visibility.Visible;
                    p.GlyphExitMode.Text = "\uE73F";
                    p.BtnExitMode.ToolTip = "Выйти из полноэкранного режима";
                    break;

                case PlayerMode.PiP:
                    p.BtnFullscreen.Visibility = Visibility.Collapsed;
                    p.BtnPip.Visibility = Visibility.Collapsed;
                    p.BtnExitMode.Visibility = Visibility.Visible;
                    p.GlyphExitMode.Text = "\uE9A6";
                    p.BtnExitMode.ToolTip = "Вернуть в основное окно";
                    break;
            }
        }

        // ── IsLocked ─────────────────────────────────────────────────────────

        public static readonly DependencyProperty IsLockedProperty =
            DependencyProperty.Register(nameof(IsLocked), typeof(bool),
                typeof(SmotrelPlayer), new PropertyMetadata(false, OnIsLockedChanged));

        public bool IsLocked
        {
            get => (bool)GetValue(IsLockedProperty);
            set => SetValue(IsLockedProperty, value);
        }

        private static void OnIsLockedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var p = (SmotrelPlayer)d;
            bool locked = (bool)e.NewValue;

            if (locked)
            {
                // Блокируем: пауза в MediaElement напрямую, PlayerState не меняем.
                // PlayerState сохраняет намерение — когда разблокируем, восстановим.
                p.Media.Pause();
                p._positionTimer.Stop();
                p.GlyphPlayPause.Text = "\uE768";
                p.LockOverlay.Visibility = Visibility.Visible;
                p.ControlsOverlay.Visibility = Visibility.Collapsed;
            }
            else
            {
                p.LockOverlay.Visibility = Visibility.Collapsed;
                p.ControlsOverlay.Visibility = Visibility.Visible;

                // Восстанавливаем состояние согласно PlayerState
                if (p._isMediaReady && p.PlayerState == PlayerState.Playing)
                {
                    p.Media.Play();
                    p._positionTimer.Start();
                    p.GlyphPlayPause.Text = "\uE769";
                }
                else
                {
                    p.GlyphPlayPause.Text = "\uE768";
                }
            }
        }

        // ── LockMessage ──────────────────────────────────────────────────────

        public static readonly DependencyProperty LockMessageProperty =
            DependencyProperty.Register(nameof(LockMessage), typeof(string),
                typeof(SmotrelPlayer),
                new PropertyMetadata("Контент недоступен", OnLockMessageChanged));

        public string LockMessage
        {
            get => (string)GetValue(LockMessageProperty);
            set => SetValue(LockMessageProperty, value);
        }

        private static void OnLockMessageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((SmotrelPlayer)d).TbLockMessage.Text = e.NewValue as string ?? string.Empty;

        // ── OverlayTimeout ───────────────────────────────────────────────────

        public static readonly DependencyProperty OverlayTimeoutProperty =
            DependencyProperty.Register(nameof(OverlayTimeout), typeof(TimeSpan),
                typeof(SmotrelPlayer),
                new PropertyMetadata(TimeSpan.FromSeconds(3), OnOverlayTimeoutChanged));

        public TimeSpan OverlayTimeout
        {
            get => (TimeSpan)GetValue(OverlayTimeoutProperty);
            set => SetValue(OverlayTimeoutProperty, value);
        }

        private static void OnOverlayTimeoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((SmotrelPlayer)d)._overlayTimer.Interval = (TimeSpan)e.NewValue;

        // ── TimelineColor ────────────────────────────────────────────────────

        public static readonly DependencyProperty TimelineColorProperty =
            DependencyProperty.Register(nameof(TimelineColor), typeof(Brush),
                typeof(SmotrelPlayer),
                new PropertyMetadata(
                    new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0x33)),
                    (d, e) => ((SmotrelPlayer)d).Timeline.ProgressBrush = (Brush)e.NewValue));

        public Brush TimelineColor
        {
            get => (Brush)GetValue(TimelineColorProperty);
            set => SetValue(TimelineColorProperty, value);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ROUTED EVENTS
        // ════════════════════════════════════════════════════════════════════════

        public static readonly RoutedEvent PlaybackStateChangedEvent =
            EventManager.RegisterRoutedEvent(nameof(PlaybackStateChanged),
                RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(SmotrelPlayer));
        public event RoutedEventHandler PlaybackStateChanged
        {
            add => AddHandler(PlaybackStateChangedEvent, value);
            remove => RemoveHandler(PlaybackStateChangedEvent, value);
        }

        public static readonly RoutedEvent PlaybackEndedEvent =
            EventManager.RegisterRoutedEvent(nameof(PlaybackEnded),
                RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(SmotrelPlayer));
        public event RoutedEventHandler PlaybackEnded
        {
            add => AddHandler(PlaybackEndedEvent, value);
            remove => RemoveHandler(PlaybackEndedEvent, value);
        }

        public static readonly RoutedEvent PreviousRequestedEvent =
            EventManager.RegisterRoutedEvent(nameof(PreviousRequested),
                RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(SmotrelPlayer));
        public event RoutedEventHandler PreviousRequested
        {
            add => AddHandler(PreviousRequestedEvent, value);
            remove => RemoveHandler(PreviousRequestedEvent, value);
        }

        public static readonly RoutedEvent NextRequestedEvent =
            EventManager.RegisterRoutedEvent(nameof(NextRequested),
                RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(SmotrelPlayer));
        public event RoutedEventHandler NextRequested
        {
            add => AddHandler(NextRequestedEvent, value);
            remove => RemoveHandler(NextRequestedEvent, value);
        }

        public static readonly RoutedEvent VolumeChangedEvent =
            EventManager.RegisterRoutedEvent(nameof(VolumeChangedRouted),
                RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(SmotrelPlayer));
        public event RoutedEventHandler VolumeChangedRouted
        {
            add => AddHandler(VolumeChangedEvent, value);
            remove => RemoveHandler(VolumeChangedEvent, value);
        }

        public static readonly RoutedEvent VideoWindowStateChangedEvent =
            EventManager.RegisterRoutedEvent(nameof(VideoWindowStateChanged),
                RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(SmotrelPlayer));
        public event RoutedEventHandler VideoWindowStateChanged
        {
            add => AddHandler(VideoWindowStateChangedEvent, value);
            remove => RemoveHandler(VideoWindowStateChangedEvent, value);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ПУБЛИЧНЫЙ API
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Основной метод загрузки видео.
        ///
        /// Что происходит:
        ///   1. Сохраняем timecodes и startPos
        ///   2. Вызываем StartLoadingSource → помечаем _isMediaReady = false,
        ///      останавливаем таймер, устанавливаем Media.Source
        ///   3. PlayerState НЕ МЕНЯЕТСЯ — MainPlayer должен задать нужное состояние
        ///      до или после вызова LoadVideo
        ///   4. Когда WPF загрузит медиа → Media_MediaOpened применит PlayerState
        /// </summary>
        public void LoadVideo(IVideo video, IList<ITimecode> timecodes,
                              TimeSpan startPosition = default)
        {
            if (video?.FilePath == null) return;

            _timecodes = timecodes ?? new List<ITimecode>();
            Timeline.Timecodes = _timecodes.Cast<ITimecode>().ToList();

            StartLoadingSource(new Uri(video.FilePath), startPosition);
        }

        /// <summary>
        /// Программный переход на позицию без поднятия событий.
        /// Можно вызывать только когда медиа уже открыта (_isMediaReady == true).
        /// </summary>
        public void SeekTo(TimeSpan position)
        {
            if (!_isMediaReady) return;
            Media.Position = position;
            SetValue(CurrentTimeProperty, position);
        }

        // ── Внутренняя инициализация загрузки ────────────────────────────────

        /// <summary>
        /// Общая точка установки нового источника.
        /// Вызывается из LoadVideo и OnFilePathChanged.
        /// </summary>
        private void StartLoadingSource(Uri uri, TimeSpan startPosition)
        {
            // Сбрасываем "готовность" медиа — до MediaOpened нельзя играть/искать
            _isMediaReady = false;
            _positionTimer.Stop();

            // Сбрасываем отображение времени
            SetValue(CurrentTimeProperty, TimeSpan.Zero);
            Timeline.Value = 0;
            Timeline.Duration = TimeSpan.Zero;

            // Сохраняем отложенный seek (применится в Media_MediaOpened)
            _pendingSeekPos = startPosition;
            _hasPendingSeek = startPosition > TimeSpan.Zero;

            Media.Source = uri;

            // Синхронизируем кнопку с текущим намерением (PlayerState)
            GlyphPlayPause.Text = PlayerState == PlayerState.Playing ? "\uE769" : "\uE768";
        }

        // ════════════════════════════════════════════════════════════════════════
        //  MEDIAELEMENT СОБЫТИЯ
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Медиа открыта и готова к воспроизведению.
        ///
        /// Вызывается WPF автоматически после установки Media.Source.
        /// Это ЕДИНСТВЕННОЕ место откуда стартует воспроизведение после загрузки файла.
        ///
        /// После этого события:
        ///   - Media.NaturalDuration.HasTimeSpan == true
        ///   - Media.Position можно читать и писать
        ///   - Media.Play() даст реальное воспроизведение
        /// </summary>
        private void Media_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (!Media.NaturalDuration.HasTimeSpan) return;

            _isMediaReady = true;

            var duration = Media.NaturalDuration.TimeSpan;
            Timeline.Duration = duration;

            // Применяем настройки, которые могли быть заданы до загрузки
            Media.Volume = IsMuted ? 0 : Volume;
            Media.SpeedRatio = _playbackSpeed;

            // Применяем отложенный seek
            if (_hasPendingSeek && _pendingSeekPos < duration)
            {
                Media.Position = _pendingSeekPos;
                SetValue(CurrentTimeProperty, _pendingSeekPos);
            }
            else
            {
                UpdateTimeLabel(TimeSpan.Zero);
            }
            _hasPendingSeek = false;
            _pendingSeekPos = TimeSpan.Zero;

            // Применяем намерение воспроизведения (PlayerState, заданный снаружи)
            // Если плеер заблокирован — не трогаем
            if (!IsLocked)
            {
                if (PlayerState == PlayerState.Playing)
                {
                    Media.Play();
                    _positionTimer.Start();
                }
                else
                {
                    Media.Pause();
                }
            }
        }

        /// <summary>
        /// Воспроизведение достигло конца файла.
        ///
        /// После этого события:
        ///   - Media.Position == NaturalDuration
        ///   - Media.Play() не даст ничего (нужен seek + play)
        ///   - Следующий LoadVideo сбросит всё и запустит новую загрузку
        ///
        /// Здесь обязательно синхронизируем PlayerState с реальностью:
        /// MediaElement уже остановился, PlayerState должен это отражать.
        /// </summary>
        private void Media_MediaEnded(object sender, RoutedEventArgs e)
        {
            _isMediaReady = false;
            _positionTimer.Stop();

            Timeline.Value = 1.0;

            // ВАЖНО: синхронизируем PlayerState — иначе иконка Play/Pause
            // останется в состоянии "играет", хотя MediaElement уже стоит.
            // SetValue обходит CoerceValue и не дублирует события если значение уже Paused.
            SetValue(PlayerStateProperty, PlayerState.Paused);
            GlyphPlayPause.Text = "\uE768";

            // Уведомляем MainPlayer — он загрузит следующее видео через LoadVideoIntoActive
            RaiseEvent(new RoutedEventArgs(PlaybackEndedEvent));
        }

        /// <summary>
        /// Ошибка загрузки или воспроизведения.
        ///
        /// Синхронизируем UI так же как при MediaEnded.
        /// </summary>
        private void Media_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
        {
            _isMediaReady = false;
            _positionTimer.Stop();

            SetValue(PlayerStateProperty, PlayerState.Paused);
            GlyphPlayPause.Text = "\uE768";
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ТАЙМЕРЫ
        // ════════════════════════════════════════════════════════════════════════

        private void OverlayTimer_Tick(object? sender, EventArgs e)
        {
            _overlayTimer.Stop();
            if (PlayerState == PlayerState.Playing && !IsLocked)
            {
                HideOverlay();
                RootGrid.Cursor = Cursors.None;
            }
        }

        /// <summary>
        /// Тикает каждые 250ms пока PlayerState == Playing и _isMediaReady.
        /// Обновляет CurrentTime, ProgressBar, метку времени.
        /// Пропускает обновление во время скраббинга (_isScrubbing).
        /// </summary>
        private void PositionTimer_Tick(object? sender, EventArgs e)
        {
            // Двойная проверка: таймер мог тикнуть после смены состояния
            if (_isScrubbing || !_isMediaReady || !Media.NaturalDuration.HasTimeSpan) return;

            var pos = Media.Position;

            // SetValue вместо свойства — чтобы не дёргать Media.Position (уже прочитали его)
            SetValue(CurrentTimeProperty, pos);
            UpdateTimeLabel(pos);
            UpdateChapterLabel(pos);
            UpdateTimelineValue(pos);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ОВЕРЛЕЙ
        // ════════════════════════════════════════════════════════════════════════

        private void ShowOverlay()
        {
            RootGrid.Cursor = null;

            ControlsOverlay.IsHitTestVisible = true;
            var anim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150));
            ControlsOverlay.BeginAnimation(OpacityProperty, anim);

            _overlayTimer.Stop();
            _overlayTimer.Start();
        }

        private void HideOverlay()
        {
            var anim = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(300));
            anim.Completed += (_, _) => ControlsOverlay.IsHitTestVisible = false;
            ControlsOverlay.BeginAnimation(OpacityProperty, anim);
        }

        private void OnMouseActivity(object sender, MouseEventArgs e) => ShowOverlay();

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

            if (e.OriginalSource is TextBlock tb && tb.Parent is Button) return;
            if (e.OriginalSource is Button) return;

            if (e.ClickCount == 1)
                TogglePlayPause();
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ГОРЯЧИЕ КЛАВИШИ
        // ════════════════════════════════════════════════════════════════════════

        public bool HandleHotkey(Key key, ModifierKeys modifiers)
        {
            if (IsLocked) return false;

            var s = AppSettings.Current;

            if (key == Key.Left && modifiers == ModifierKeys.Shift) { RaiseEvent(new RoutedEventArgs(PreviousRequestedEvent)); return true; }
            if (key == Key.Right && modifiers == ModifierKeys.Shift) { RaiseEvent(new RoutedEventArgs(NextRequestedEvent)); return true; }

            if (modifiers != ModifierKeys.None) return false;

            switch (key)
            {
                case Key.Space:
                    TogglePlayPause();
                    return true;

                case Key.Right:
                    Seek(TimeSpan.FromSeconds(s.SeekForwardSeconds));
                    return true;

                case Key.Left:
                    Seek(TimeSpan.FromSeconds(-s.SeekBackwardSeconds));
                    return true;

                case Key.F:
                    if (PlayerMode == PlayerMode.Normal) RequestWindowState(VideoWindowStateRequest.Fullscreen);
                    else if (PlayerMode == PlayerMode.Fullscreen) RequestWindowState(VideoWindowStateRequest.Normal);
                    return true;

                case Key.P:
                    if (PlayerMode == PlayerMode.Normal) RequestWindowState(VideoWindowStateRequest.PiP);
                    else if (PlayerMode == PlayerMode.PiP) RequestWindowState(VideoWindowStateRequest.Normal);
                    return true;

                case Key.Escape:
                    if (PlayerMode != PlayerMode.Normal)
                        RequestWindowState(VideoWindowStateRequest.Normal);
                    return true;
            }

            return false;
        }

        private void Seek(TimeSpan delta)
        {
            if (!_isMediaReady || !Media.NaturalDuration.HasTimeSpan) return;

            var total = Media.NaturalDuration.TimeSpan;
            var pos = Media.Position + delta;
            pos = pos < TimeSpan.Zero ? TimeSpan.Zero : pos > total ? total : pos;

            Media.Position = pos;
            SetValue(CurrentTimeProperty, pos);
            UpdateTimeLabel(pos);
            UpdateTimelineValue(pos);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  СКРАББИНГ (таймлайн)
        // ════════════════════════════════════════════════════════════════════════
        //
        //  SeekStarted / SeekCompleted от ClickableProgressBar.
        //
        //  Принцип: НЕ меняем PlayerState во время скраббинга.
        //  Это исключает паразитные PlaybackStateChangedEvent в MainPlayer.
        //  Вместо этого управляем MediaElement напрямую.

        private void Timeline_SeekStarted(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // Глушим звук, чтобы не было треска при перемотке
            _volumeBeforeScrub = IsMuted ? 0 : Volume;
            Media.Volume = 0;

            // Ставим на паузу напрямую (PlayerState не трогаем!)
            if (_isMediaReady)
                Media.Pause();

            _isScrubbing = true;
        }

        private void Timeline_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // Вызывается при каждом движении ползунка (после SeekStarted)
            if (!_isMediaReady || !Media.NaturalDuration.HasTimeSpan) return;

            var pos = TimeSpan.FromSeconds(
                Media.NaturalDuration.TimeSpan.TotalSeconds * e.NewValue);

            Media.Position = pos;
            UpdateTimeLabel(pos);
            UpdateChapterLabel(pos);
        }

        private void Timeline_SeekCompleted(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _isScrubbing = false;

            // Восстанавливаем громкость
            Media.Volume = _volumeBeforeScrub;

            if (!_isMediaReady) return;

            // Применяем финальную позицию
            var pos = TimeSpan.FromSeconds(
                Media.NaturalDuration.TimeSpan.TotalSeconds * e.NewValue);
            Media.Position = pos;
            SetValue(CurrentTimeProperty, pos);

            // Возобновляем воспроизведение если так задан PlayerState
            if (PlayerState == PlayerState.Playing && !IsLocked)
            {
                Media.Play();
                _positionTimer.Start();
            }
        }

        // ── Громкость ─────────────────────────────────────────────────────────

        private void VolumeBar_ValueChanged(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (IsMuted && e.NewValue > 0) IsMuted = false;
            Volume = e.NewValue;
            Media.Volume = IsMuted ? 0 : e.NewValue;
            UpdateVolumeGlyph();
            RaiseEvent(new RoutedEventArgs(VolumeChangedEvent));
        }

        // ════════════════════════════════════════════════════════════════════════
        //  АНИМАЦИЯ PLAY / PAUSE
        // ════════════════════════════════════════════════════════════════════════

        private void RunPlayStateAnimation(PlayerState newState)
        {
            PlayStateGlyph.Text = newState == PlayerState.Playing ? "\uE769" : "\uE768";

            var sb = new Storyboard();

            void AddVis(bool show, double atMs)
            {
                var v = new ObjectAnimationUsingKeyFrames();
                Storyboard.SetTarget(v, PlayStateGlyphHost);
                Storyboard.SetTargetProperty(v, new PropertyPath(VisibilityProperty));
                v.KeyFrames.Add(new DiscreteObjectKeyFrame(
                    show ? Visibility.Visible : Visibility.Collapsed,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(atMs))));
                sb.Children.Add(v);
            }

            void AddDouble(DependencyObject target, PropertyPath prop,
                double from, double to, double beginMs, double durationMs,
                IEasingFunction? ease = null)
            {
                var a = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(durationMs))
                {
                    BeginTime = TimeSpan.FromMilliseconds(beginMs),
                    EasingFunction = ease,
                };
                Storyboard.SetTarget(a, target);
                Storyboard.SetTargetProperty(a, prop);
                sb.Children.Add(a);
            }

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            AddVis(true, 0);
            AddDouble(PlayStateGlyphHost, new PropertyPath(OpacityProperty), 0, 1, 0, 80);
            AddDouble(PlayStateScale, new PropertyPath(ScaleTransform.ScaleXProperty), 0.6, 1.0, 0, 180, ease);
            AddDouble(PlayStateScale, new PropertyPath(ScaleTransform.ScaleYProperty), 0.6, 1.0, 0, 180, ease);
            AddDouble(PlayStateGlyphHost, new PropertyPath(OpacityProperty), 1, 0, 480, 220);
            AddVis(false, 710);

            sb.Begin();
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ОБНОВЛЕНИЕ UI
        // ════════════════════════════════════════════════════════════════════════

        private void UpdateTimeLabel(TimeSpan current)
        {
            var total = Media.NaturalDuration.HasTimeSpan
                ? Media.NaturalDuration.TimeSpan : TimeSpan.Zero;
            TbTime.Text = $"{Fmt(current)} / {Fmt(total)}";
        }

        private void UpdateChapterLabel(TimeSpan current)
        {
            ITimecode? active = null;
            foreach (var tc in _timecodes)
            {
                if (tc.Position <= current) active = tc;
                else break;
            }
            if (active != null) { TbChapter.Text = active.Label; TbChapter.Opacity = 1; }
            else TbChapter.Opacity = 0;
        }

        private void UpdateTimelineValue(TimeSpan current)
        {
            if (_isScrubbing || !_isMediaReady || !Media.NaturalDuration.HasTimeSpan) return;
            var total = Media.NaturalDuration.TimeSpan;
            if (total.TotalSeconds > 0)
                Timeline.Value = current.TotalSeconds / total.TotalSeconds;
        }

        private void UpdateVolumeGlyph()
        {
            double vol = IsMuted ? 0.0 : Volume;
            GlyphVolume.Text = vol switch
            {
                0.0 => "\uE992",
                <= 0.33 => "\uE993",
                <= 0.66 => "\uE994",
                _ => "\uE995",
            };
        }

        private static string Fmt(TimeSpan ts) =>
            ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes}:{ts.Seconds:D2}";

        private void TogglePlayPause()
        {
            if (IsLocked) return;
            PlayerState = PlayerState == PlayerState.Playing
                ? PlayerState.Paused : PlayerState.Playing;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  КНОПКИ НИЖНЕЙ ПАНЕЛИ
        // ════════════════════════════════════════════════════════════════════════

        private void BtnPlayPause_Click(object sender, RoutedEventArgs e) => TogglePlayPause();

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
            => RaiseEvent(new RoutedEventArgs(PreviousRequestedEvent));

        private void BtnNext_Click(object sender, RoutedEventArgs e)
            => RaiseEvent(new RoutedEventArgs(NextRequestedEvent));

        private void BtnMute_Click(object sender, RoutedEventArgs e)
        {
            IsMuted = !IsMuted;
            RaiseEvent(new RoutedEventArgs(VolumeChangedEvent));
        }

        private void BtnFullscreen_Click(object sender, RoutedEventArgs e)
            => RequestWindowState(VideoWindowStateRequest.Fullscreen);

        private void BtnPip_Click(object sender, RoutedEventArgs e)
            => RequestWindowState(VideoWindowStateRequest.PiP);

        private void BtnExitMode_Click(object sender, RoutedEventArgs e)
            => RequestWindowState(VideoWindowStateRequest.Normal);

        private void RequestWindowState(VideoWindowStateRequest request)
            => RaiseEvent(new VideoWindowStateRequestEventArgs(VideoWindowStateChangedEvent, request));

        private void BtnSpeed_Click(object sender, RoutedEventArgs e)
        {
            if (BtnSpeed.ContextMenu is { } m)
            {
                m.PlacementTarget = BtnSpeed;
                m.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
                m.IsOpen = true;
            }
        }

        private void SpeedItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item
                && item.Tag is string tag
                && double.TryParse(tag, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double speed))
            {
                _playbackSpeed = speed;
                TbSpeed.Text = speed == 1.0 ? "1×" : $"{speed}×";
                Media.SpeedRatio = speed;
                foreach (MenuItem mi in SpeedMenu.Items) mi.IsChecked = mi == item;
            }
        }

        // ── Lock overlay ──────────────────────────────────────────────────────

        private void LockOverlay_PreviewMouseDown(object sender, MouseButtonEventArgs e)
            => e.Handled = true;

        private void LockOverlay_PreviewKeyDown(object sender, KeyEventArgs e)
            => e.Handled = true;
    }

    // ── Вспомогательные типы для события смены режима ─────────────────────────

    public enum VideoWindowStateRequest { Normal, Fullscreen, PiP }

    public class VideoWindowStateRequestEventArgs : RoutedEventArgs
    {
        public VideoWindowStateRequest Request { get; }
        public VideoWindowStateRequestEventArgs(RoutedEvent re, VideoWindowStateRequest req)
            : base(re) { Request = req; }
    }
}