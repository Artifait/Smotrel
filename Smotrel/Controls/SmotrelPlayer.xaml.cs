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
        private readonly DispatcherTimer _scrubEndTimer = new();

        private IList<ITimecode> _timecodes = new List<ITimecode>();
        private PlayerState _stateBeforeLock = PlayerState.Paused;
        private double _volumeBeforeMute = 1.0;
        private double _playbackSpeed = 1.0;
        private bool _isScrubbing;
        private double _volumeBeforeScrub = 1.0; // для mute во время скраббинга

        // ── Конструктор ───────────────────────────────────────────────────────

        public SmotrelPlayer()
        {
            InitializeComponent();

            _overlayTimer.Tick += OverlayTimer_Tick;
            _overlayTimer.Interval = OverlayTimeout;

            _positionTimer.Interval = TimeSpan.FromMilliseconds(250);
            _positionTimer.Tick += PositionTimer_Tick;

            // Таймер завершения скраббинга — восстанавливает звук через 120ms
            // после последнего движения ползунка
            _scrubEndTimer.Interval = TimeSpan.FromMilliseconds(120);
            _scrubEndTimer.Tick += ScrubEndTimer_Tick;

            MouseMove += OnMouseActivity;
            MouseDown += OnMouseActivity;

            VolumeBar.Value = Volume;
            UpdateVolumeGlyph();

            Loaded += (_, _) => { ShowOverlay(); Focus(); };
        }

        // ════════════════════════════════════════════════════════════════════════
        //  DEPENDENCY PROPERTIES
        // ════════════════════════════════════════════════════════════════════════

        // ── FilePath ─────────────────────────────────────────────────────────

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
            var p = (SmotrelPlayer)d;
            if (e.NewValue is string path && !string.IsNullOrWhiteSpace(path))
                p.Media.Source = new Uri(path);
            else
                p.Media.Source = null;
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
            if (!p._isScrubbing && p.Media.NaturalDuration.HasTimeSpan)
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
                p.Media.Play();
                p._positionTimer.Start();
                p.GlyphPlayPause.Text = "\uE769";
            }
            else
            {
                p.Media.Pause();
                p._positionTimer.Stop();
                p.GlyphPlayPause.Text = "\uE768";
            }

            p.RunPlayStateAnimation(newState);
            p.RaiseEvent(new PlayerStateChangedEventArgs(PlaybackStateChangedEvent, oldState, newState));
        }

        // ── PlayerMode — управляет видимостью кнопок ─────────────────────────

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
                    // Все кнопки видны, кнопки Fullscreen/PiP видны, ExitMode — нет
                    p.BtnFullscreen.Visibility = Visibility.Visible;
                    p.BtnPip.Visibility = Visibility.Visible;
                    p.BtnExitMode.Visibility = Visibility.Collapsed;
                    break;

                case PlayerMode.Fullscreen:
                    // Только кнопка выхода из fullscreen (E73F = "свернуть окно")
                    p.BtnFullscreen.Visibility = Visibility.Collapsed;
                    p.BtnPip.Visibility = Visibility.Collapsed;
                    p.BtnExitMode.Visibility = Visibility.Visible;
                    p.GlyphExitMode.Text = "\uE73F"; // collapse / exit fullscreen
                    p.BtnExitMode.ToolTip = "Выйти из полноэкранного режима";
                    break;

                case PlayerMode.PiP:
                    // Только кнопка возврата в основное окно (E9A6)
                    p.BtnFullscreen.Visibility = Visibility.Collapsed;
                    p.BtnPip.Visibility = Visibility.Collapsed;
                    p.BtnExitMode.Visibility = Visibility.Visible;
                    p.GlyphExitMode.Text = "\uE9A6"; // picture in picture exit
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
                p._stateBeforeLock = p.PlayerState;
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
                if (p._stateBeforeLock == PlayerState.Playing)
                    p.PlayerState = PlayerState.Playing;
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

        /// <summary>
        /// Пользователь запросил смену режима окна.
        /// MainPlayer обрабатывает этот ивент, сам SmotrelPlayer ничего не перемещает.
        /// </summary>
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

        public void LoadVideo(IVideo video, IEnumerable<ITimecode>? timecodes = null)
        {
            _timecodes = timecodes?.ToList() ?? new List<ITimecode>();
            Timeline.Timecodes = _timecodes;
            Title = video.Title;
            FilePath = video.FilePath;
            SetValue(CurrentTimeProperty, TimeSpan.Zero);
        }

        /// <summary>Программный переход на позицию (без поднятия события).</summary>
        public void SeekTo(TimeSpan position)
        {
            if (Media.NaturalDuration.HasTimeSpan)
                Media.Position = position;
            SetValue(CurrentTimeProperty, position);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  MEDIAELMENT СОБЫТИЯ
        // ════════════════════════════════════════════════════════════════════════

        private void Media_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (!Media.NaturalDuration.HasTimeSpan) return;

            Timeline.Duration = Media.NaturalDuration.TimeSpan;
            UpdateTimeLabel(TimeSpan.Zero);

            Media.Volume = IsMuted ? 0 : Volume;
            Media.SpeedRatio = _playbackSpeed;

            // Дефолт PlayerState = Playing → callback запустит Media.Play()
            PlayerState = PlayerState.Playing;
        }

        private void Media_MediaEnded(object sender, RoutedEventArgs e)
        {
            _positionTimer.Stop();
            // Не меняем PlayerState — пусть MainPlayer решает что делать
            Timeline.Value = 1.0;
            RaiseEvent(new RoutedEventArgs(PlaybackEndedEvent));
        }

        private void Media_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
        {
            _positionTimer.Stop();
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ТАЙМЕРЫ
        // ════════════════════════════════════════════════════════════════════════

        private void OverlayTimer_Tick(object? sender, EventArgs e)
        {
            _overlayTimer.Stop();
            if (PlayerState == PlayerState.Playing)
            {
                HideOverlay();
                // Скрываем курсор вместе с оверлеем
                RootGrid.Cursor = Cursors.None;
            }
        }

        private void PositionTimer_Tick(object? sender, EventArgs e)
        {
            if (_isScrubbing || !Media.NaturalDuration.HasTimeSpan) return;

            var pos = Media.Position;
            SetValue(CurrentTimeProperty, pos);
            UpdateTimeLabel(pos);
            UpdateChapterLabel(pos);
            UpdateTimelineValue(pos);
        }

        private void ScrubEndTimer_Tick(object? sender, EventArgs e)
        {
            _scrubEndTimer.Stop();
            _isScrubbing = false;

            // Восстанавливаем громкость после скраббинга
            Media.Volume = IsMuted ? 0 : _volumeBeforeScrub;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ОВЕРЛЕЙ
        // ════════════════════════════════════════════════════════════════════════

        private void ShowOverlay()
        {
            // Возвращаем курсор
            RootGrid.Cursor = null; // наследует от родителя (обычно Arrow)

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

        private void OnMouseActivity(object sender, MouseEventArgs e)
        {
            ShowOverlay();
        }

        // ── Одиночный клик = Play/Pause ───────────────────────────────────────

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

            // Кликнули на кнопку управления — не реагируем
            if (e.OriginalSource is TextBlock tb && tb.Parent is Button) return;
            if (e.OriginalSource is Button) return;

            if (e.ClickCount == 1)
            {
                TogglePlayPause();
                // e.Handled остаётся false — PipPlayer может вызвать DragMove
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ГОРЯЧИЕ КЛАВИШИ (вызывается из MainPlayer/PipPlayer)
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Обрабатывает горячую клавишу. Вызывать только когда игрок в фокусе.
        /// </summary>
        public bool HandleHotkey(Key key, ModifierKeys modifiers)
        {
            if (IsLocked) return false;

            var s = AppSettings.Current;

            // Shift+Left = предыдущее видео
            if (key == Key.Left && modifiers == ModifierKeys.Shift)
            {
                RaiseEvent(new RoutedEventArgs(PreviousRequestedEvent));
                return true;
            }
            // Shift+Right = следующее видео
            if (key == Key.Right && modifiers == ModifierKeys.Shift)
            {
                RaiseEvent(new RoutedEventArgs(NextRequestedEvent));
                return true;
            }

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
                    if (PlayerMode == PlayerMode.Normal)
                        RequestWindowState(VideoWindowStateRequest.Fullscreen);
                    else if (PlayerMode == PlayerMode.Fullscreen)
                        RequestWindowState(VideoWindowStateRequest.Normal);
                    return true;

                case Key.P:
                    if (PlayerMode == PlayerMode.Normal)
                        RequestWindowState(VideoWindowStateRequest.PiP);
                    else if (PlayerMode == PlayerMode.PiP)
                        RequestWindowState(VideoWindowStateRequest.Normal);
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
            if (!Media.NaturalDuration.HasTimeSpan) return;

            var total = Media.NaturalDuration.TimeSpan;
            var pos = Media.Position + delta;
            pos = pos < TimeSpan.Zero ? TimeSpan.Zero
                : pos > total ? total
                : pos;

            Media.Position = pos;
            SetValue(CurrentTimeProperty, pos);
            UpdateTimeLabel(pos);
            UpdateTimelineValue(pos);
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
            if (_isScrubbing || !Media.NaturalDuration.HasTimeSpan) return;
            var total = Media.NaturalDuration.TimeSpan;
            if (total.TotalSeconds > 0)
                Timeline.Value = current.TotalSeconds / total.TotalSeconds;
        }

        private void UpdateVolumeGlyph()
        {
            double vol = IsMuted ? 0.0 : Volume;
            GlyphVolume.Text = vol switch
            {
                0.0 => "\uE970",
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
        {
            var args = new VideoWindowStateRequestEventArgs(VideoWindowStateChangedEvent, request);
            RaiseEvent(args);
        }

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

        // ── Таймлайн ─────────────────────────────────────────────────────────

        private void Timeline_ValueChanged(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (!Media.NaturalDuration.HasTimeSpan) return;

            // Начало скраббинга: глушим звук чтобы не было треска
            if (!_isScrubbing)
            {
                _volumeBeforeScrub = IsMuted ? 0 : Volume;
                Media.Volume = 0;
                _isScrubbing = true;
            }

            var pos = TimeSpan.FromSeconds(
                Media.NaturalDuration.TimeSpan.TotalSeconds * e.NewValue);

            Media.Position = pos;
            UpdateTimeLabel(pos);
            UpdateChapterLabel(pos);

            // Перезапускаем таймер окончания скраббинга
            _scrubEndTimer.Stop();
            _scrubEndTimer.Start();
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