using Smotrel.Enums;
using Smotrel.Events;
using Smotrel.Interfaces;
using Smotrel.Models;
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

        /// <summary>
        /// Таймер для скрытия оверлея управления.
        /// Сбрасывается при каждом движении мыши.
        /// </summary>
        private readonly DispatcherTimer _overlayTimer = new();

        /// <summary>
        /// Таймер для обновления позиции таймлайна и временной метки во время воспроизведения.
        /// Тикает каждые 250 мс.
        /// </summary>
        private readonly DispatcherTimer _positionTimer = new();

        /// <summary>Список таймкодов текущего видео.</summary>
        private IList<ITimecode> _timecodes = new List<ITimecode>();

        /// <summary>Состояние до блокировки — восстанавливается при разблокировке.</summary>
        private PlayerState _stateBeforeLock = PlayerState.Paused;

        /// <summary>Громкость до mute — восстанавливается при снятии mute.</summary>
        private double _volumeBeforeMute = 1.0;

        /// <summary>Текущая скорость воспроизведения.</summary>
        private double _playbackSpeed = 1.0;

        /// <summary>Флаг: пользователь тащит таймлайн — не обновляем позицию из Media.</summary>
        private bool _isScrubbing;

        // Для Reparenting (Fullscreen / Normal)
        private Panel?  _originalParent;
        private int     _originalChildIndex;
        private Window? _fullscreenWindow;
        private Window? _pipWindow;

        // ── Конструктор ───────────────────────────────────────────────────────

        public SmotrelPlayer()
        {
            InitializeComponent();

            // Оверлей-таймер
            _overlayTimer.Tick += OverlayTimer_Tick;

            // Позиция-таймер
            _positionTimer.Interval = TimeSpan.FromMilliseconds(250);
            _positionTimer.Tick    += PositionTimer_Tick;

            // Применяем дефолтный OverlayTimeout из DP
            _overlayTimer.Interval = OverlayTimeout;

            // Мышь
            MouseMove  += OnMouseActivity;
            MouseDown  += OnMouseActivity;

            // Начальные значения UI
            VolumeBar.Value = Volume;
            UpdateVolumeGlyph();

            Loaded += (_, _) => ShowOverlay();
        }

        // ════════════════════════════════════════════════════════════════════════
        //  DEPENDENCY PROPERTIES
        // ════════════════════════════════════════════════════════════════════════

        // ── FilePath ─────────────────────────────────────────────────────────

        public static readonly DependencyProperty FilePathProperty =
            DependencyProperty.Register(
                nameof(FilePath), typeof(string), typeof(SmotrelPlayer),
                new PropertyMetadata(null, OnFilePathChanged));

        public string? FilePath
        {
            get => (string?)GetValue(FilePathProperty);
            set => SetValue(FilePathProperty, value);
        }

        private static void OnFilePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var p = (SmotrelPlayer)d;
            if (e.NewValue is string path && !string.IsNullOrWhiteSpace(path))
            {
                p.Media.Source = new Uri(path);
                // MediaOpened запустит воспроизведение
            }
            else
            {
                p.Media.Source = null;
            }
        }

        // ── Title ────────────────────────────────────────────────────────────

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(
                nameof(Title), typeof(string), typeof(SmotrelPlayer),
                new PropertyMetadata(string.Empty, OnTitleChanged));

        public new string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((SmotrelPlayer)d).TbTitle.Text = e.NewValue as string ?? string.Empty;

        // ── CurrentTime ──────────────────────────────────────────────────────

        public static readonly DependencyProperty CurrentTimeProperty =
            DependencyProperty.Register(
                nameof(CurrentTime), typeof(TimeSpan), typeof(SmotrelPlayer),
                new PropertyMetadata(TimeSpan.Zero, OnCurrentTimeChanged));

        public TimeSpan CurrentTime
        {
            get => (TimeSpan)GetValue(CurrentTimeProperty);
            set => SetValue(CurrentTimeProperty, value);
        }

        private static void OnCurrentTimeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var p   = (SmotrelPlayer)d;
            var pos = (TimeSpan)e.NewValue;

            // Синхронизируем MediaElement если не скраббинг
            if (!p._isScrubbing && p.Media.NaturalDuration.HasTimeSpan)
            {
                p.Media.Position = pos;
            }

            p.UpdateTimeLabel(pos);
            p.UpdateChapterLabel(pos);
            p.UpdateTimelineValue(pos);
        }

        // ── Volume ───────────────────────────────────────────────────────────

        public static readonly DependencyProperty VolumeProperty =
            DependencyProperty.Register(
                nameof(Volume), typeof(double), typeof(SmotrelPlayer),
                new PropertyMetadata(1.0, OnVolumeChanged),
                v => (double)v >= 0.0 && (double)v <= 1.0);

        public double Volume
        {
            get => (double)GetValue(VolumeProperty);
            set => SetValue(VolumeProperty, value);
        }

        private static void OnVolumeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var p = (SmotrelPlayer)d;
            p.Media.Volume      = (double)e.NewValue;
            p.VolumeBar.Value   = (double)e.NewValue;
            p.UpdateVolumeGlyph();
        }

        // ── IsMuted ──────────────────────────────────────────────────────────

        public static readonly DependencyProperty IsMutedProperty =
            DependencyProperty.Register(
                nameof(IsMuted), typeof(bool), typeof(SmotrelPlayer),
                new PropertyMetadata(false, OnIsMutedChanged));

        public bool IsMuted
        {
            get => (bool)GetValue(IsMutedProperty);
            set => SetValue(IsMutedProperty, value);
        }

        private static void OnIsMutedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var p      = (SmotrelPlayer)d;
            bool muted = (bool)e.NewValue;

            if (muted)
            {
                p._volumeBeforeMute = p.Volume;
                p.Media.Volume      = 0;
                p.VolumeBar.Value   = 0;
            }
            else
            {
                p.Volume          = p._volumeBeforeMute;
                p.Media.Volume    = p._volumeBeforeMute;
                p.VolumeBar.Value = p._volumeBeforeMute;
            }

            p.UpdateVolumeGlyph();
        }

        // ── PlayerState ──────────────────────────────────────────────────────

        public static readonly DependencyProperty PlayerStateProperty =
            DependencyProperty.Register(
                nameof(PlayerState), typeof(PlayerState), typeof(SmotrelPlayer),
                new PropertyMetadata(PlayerState.Paused, OnPlayerStateChanged));

        public PlayerState PlayerState
        {
            get => (PlayerState)GetValue(PlayerStateProperty);
            set => SetValue(PlayerStateProperty, value);
        }

        private static void OnPlayerStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var p        = (SmotrelPlayer)d;
            var oldState = (PlayerState)e.OldValue;
            var newState = (PlayerState)e.NewValue;

            if (newState == PlayerState.Playing)
            {
                p.Media.Play();
                p._positionTimer.Start();
                p.GlyphPlayPause.Text = "\uE769"; // Pause glyph
            }
            else
            {
                p.Media.Pause();
                p._positionTimer.Stop();
                p.GlyphPlayPause.Text = "\uE768"; // Play glyph
            }

            // Запускаем анимацию центрального глифа
            p.RunPlayStateAnimation(newState);

            // Поднимаем событие
            p.RaiseEvent(new PlayerStateChangedEventArgs(
                PlaybackStateChangedEvent, oldState, newState));
        }

        // ── VideoWindowState ─────────────────────────────────────────────────

        public static readonly DependencyProperty VideoWindowStateProperty =
            DependencyProperty.Register(
                nameof(VideoWindowState), typeof(VideoWindowState), typeof(SmotrelPlayer),
                new PropertyMetadata(VideoWindowState.Normal, OnVideoWindowStateChanged));

        public VideoWindowState VideoWindowState
        {
            get => (VideoWindowState)GetValue(VideoWindowStateProperty);
            set => SetValue(VideoWindowStateProperty, value);
        }

        private static void OnVideoWindowStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var p        = (SmotrelPlayer)d;
            var oldState = (VideoWindowState)e.OldValue;
            var newState = (VideoWindowState)e.NewValue;

            switch (newState)
            {
                case VideoWindowState.Maximized: p.EnterFullscreen();  break;
                case VideoWindowState.PiP:       p.EnterPiP();         break;
                case VideoWindowState.Normal:    p.ExitToNormal();     break;
            }

            // Кнопки: BtnNormal виден только когда НЕ Normal
            p.BtnNormal.Visibility = newState == VideoWindowState.Normal
                ? Visibility.Collapsed
                : Visibility.Visible;

            p.RaiseEvent(new VideoWindowStateChangedEventArgs(
                VideoWindowStateChangedEvent, oldState, newState));
        }

        // ── IsLocked ─────────────────────────────────────────────────────────

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
            var p      = (SmotrelPlayer)d;
            bool locked = (bool)e.NewValue;

            if (locked)
            {
                // Запоминаем состояние, останавливаем воспроизведение
                p._stateBeforeLock = p.PlayerState;
                p.Media.Pause();
                p._positionTimer.Stop();
                p.GlyphPlayPause.Text = "\uE768";

                p.LockOverlay.Visibility    = Visibility.Visible;
                p.ControlsOverlay.Visibility = Visibility.Collapsed;
            }
            else
            {
                p.LockOverlay.Visibility     = Visibility.Collapsed;
                p.ControlsOverlay.Visibility = Visibility.Visible;

                // Восстанавливаем состояние до блокировки
                if (p._stateBeforeLock == PlayerState.Playing)
                {
                    p.PlayerState = PlayerState.Playing;
                }
            }
        }

        // ── LockMessage ──────────────────────────────────────────────────────

        public static readonly DependencyProperty LockMessageProperty =
            DependencyProperty.Register(
                nameof(LockMessage), typeof(string), typeof(SmotrelPlayer),
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
            DependencyProperty.Register(
                nameof(OverlayTimeout), typeof(TimeSpan), typeof(SmotrelPlayer),
                new PropertyMetadata(TimeSpan.FromSeconds(3), OnOverlayTimeoutChanged));

        public TimeSpan OverlayTimeout
        {
            get => (TimeSpan)GetValue(OverlayTimeoutProperty);
            set => SetValue(OverlayTimeoutProperty, value);
        }

        private static void OnOverlayTimeoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((SmotrelPlayer)d)._overlayTimer.Interval = (TimeSpan)e.NewValue;

        // ── TimelineColor (кастомный цвет прогрессбара) ──────────────────────

        public static readonly DependencyProperty TimelineColorProperty =
            DependencyProperty.Register(
                nameof(TimelineColor), typeof(Brush), typeof(SmotrelPlayer),
                new PropertyMetadata(
                    new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0x33)),
                    OnTimelineColorChanged));

        public Brush TimelineColor
        {
            get => (Brush)GetValue(TimelineColorProperty);
            set => SetValue(TimelineColorProperty, value);
        }

        private static void OnTimelineColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((SmotrelPlayer)d).Timeline.ProgressBrush = (Brush)e.NewValue;

        // ════════════════════════════════════════════════════════════════════════
        //  ROUTED EVENTS  (RoutingStrategy.Bubble)
        // ════════════════════════════════════════════════════════════════════════

        public static readonly RoutedEvent PlaybackStateChangedEvent =
            EventManager.RegisterRoutedEvent(
                nameof(PlaybackStateChanged), RoutingStrategy.Bubble,
                typeof(EventHandler<PlayerStateChangedEventArgs>), typeof(SmotrelPlayer));

        public event EventHandler<PlayerStateChangedEventArgs> PlaybackStateChanged
        {
            add    => AddHandler(PlaybackStateChangedEvent, value);
            remove => RemoveHandler(PlaybackStateChangedEvent, value);
        }

        public static readonly RoutedEvent PlaybackEndedEvent =
            EventManager.RegisterRoutedEvent(
                nameof(PlaybackEnded), RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(SmotrelPlayer));

        public event RoutedEventHandler PlaybackEnded
        {
            add    => AddHandler(PlaybackEndedEvent, value);
            remove => RemoveHandler(PlaybackEndedEvent, value);
        }

        public static readonly RoutedEvent PreviousRequestedEvent =
            EventManager.RegisterRoutedEvent(
                nameof(PreviousRequested), RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(SmotrelPlayer));

        public event RoutedEventHandler PreviousRequested
        {
            add    => AddHandler(PreviousRequestedEvent, value);
            remove => RemoveHandler(PreviousRequestedEvent, value);
        }

        public static readonly RoutedEvent NextRequestedEvent =
            EventManager.RegisterRoutedEvent(
                nameof(NextRequested), RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(SmotrelPlayer));

        public event RoutedEventHandler NextRequested
        {
            add    => AddHandler(NextRequestedEvent, value);
            remove => RemoveHandler(NextRequestedEvent, value);
        }

        public static readonly RoutedEvent VolumeChangedEvent =
            EventManager.RegisterRoutedEvent(
                nameof(VolumeChangedRouted), RoutingStrategy.Bubble,
                typeof(EventHandler<VolumeChangedEventArgs>), typeof(SmotrelPlayer));

        public event EventHandler<VolumeChangedEventArgs> VolumeChangedRouted
        {
            add    => AddHandler(VolumeChangedEvent, value);
            remove => RemoveHandler(VolumeChangedEvent, value);
        }

        public static readonly RoutedEvent VideoWindowStateChangedEvent =
            EventManager.RegisterRoutedEvent(
                nameof(VideoWindowStateChanged), RoutingStrategy.Bubble,
                typeof(EventHandler<VideoWindowStateChangedEventArgs>), typeof(SmotrelPlayer));

        public event EventHandler<VideoWindowStateChangedEventArgs> VideoWindowStateChanged
        {
            add    => AddHandler(VideoWindowStateChangedEvent, value);
            remove => RemoveHandler(VideoWindowStateChangedEvent, value);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ПУБЛИЧНЫЙ API
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Загружает видео и таймкоды. Сбрасывает позицию и запускает воспроизведение.
        /// Основная точка входа — вручную задавать DP не нужно.
        /// </summary>
        public void LoadVideo(IVideo video, IEnumerable<ITimecode>? timecodes = null)
        {
            _timecodes = timecodes?.ToList() ?? new List<ITimecode>();

            Title    = video.Title;
            FilePath = video.FilePath; // → OnFilePathChanged → Media.Source = new Uri(...)

            Timeline.Timecodes = _timecodes;

            // Сброс позиции и состояния
            CurrentTime = TimeSpan.Zero;
            // Воспроизведение запустится в Media_MediaOpened
        }

        // ════════════════════════════════════════════════════════════════════════
        //  MEDIAELMENT СОБЫТИЯ
        // ════════════════════════════════════════════════════════════════════════

        private void Media_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (!Media.NaturalDuration.HasTimeSpan) return;

            var duration = Media.NaturalDuration.TimeSpan;

            Timeline.Duration = duration;
            UpdateTimeLabel(TimeSpan.Zero);

            // Применяем громкость и скорость
            Media.Volume      = IsMuted ? 0 : Volume;
            Media.SpeedRatio  = _playbackSpeed;

            // Запускаем воспроизведение
            PlayerState = PlayerState.Playing;
        }

        private void Media_MediaEnded(object sender, RoutedEventArgs e)
        {
            PlayerState = PlayerState.Paused;
            CurrentTime = TimeSpan.Zero;
            Timeline.Value = 0;

            RaiseEvent(new RoutedEventArgs(PlaybackEndedEvent));
        }

        private void Media_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
        {
            // TODO: показать сообщение об ошибке
            PlayerState = PlayerState.Paused;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ТАЙМЕРЫ
        // ════════════════════════════════════════════════════════════════════════

        private void OverlayTimer_Tick(object? sender, EventArgs e)
        {
            _overlayTimer.Stop();
            // Скрываем оверлей только если идёт воспроизведение
            if (PlayerState == PlayerState.Playing)
                HideOverlay();
        }

        private void PositionTimer_Tick(object? sender, EventArgs e)
        {
            if (_isScrubbing || !Media.NaturalDuration.HasTimeSpan) return;

            var pos = Media.Position;

            // Обновляем без PropertyChangedCallback (чтобы не перемещать Media.Position)
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
            ControlsOverlay.IsHitTestVisible = true;

            var anim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150));
            ControlsOverlay.BeginAnimation(OpacityProperty, anim);

            _overlayTimer.Stop();
            _overlayTimer.Start();
        }

        private void HideOverlay()
        {
            var anim = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(300));
            anim.Completed += (_, _) =>
            {
                ControlsOverlay.IsHitTestVisible = false;
            };
            ControlsOverlay.BeginAnimation(OpacityProperty, anim);
        }

        private void OnMouseActivity(object sender, MouseEventArgs e)
        {
            ShowOverlay();
        }

        // ── Двойной клик: переключение Play/Pause ─────────────────────────────

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

            if (e.ClickCount == 2)
            {
                TogglePlayPause();
                e.Handled = true;
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  АНИМАЦИЯ СМЕНЫ СОСТОЯНИЯ (центральный глиф)
        // ════════════════════════════════════════════════════════════════════════

        private void RunPlayStateAnimation(PlayerState newState)
        {
            // Глиф: Play→показываем ▶, Pause→показываем ⏸
            PlayStateGlyph.Text = newState == PlayerState.Playing ? "\uE769" : "\uE768";

            var sb = new Storyboard();

            // Показать Host
            var showVis = new ObjectAnimationUsingKeyFrames();
            Storyboard.SetTarget(showVis, PlayStateGlyphHost);
            Storyboard.SetTargetProperty(showVis, new PropertyPath(VisibilityProperty));
            showVis.KeyFrames.Add(new DiscreteObjectKeyFrame(Visibility.Visible, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            sb.Children.Add(showVis);

            // Opacity: 0 → 1 за 80ms
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(80));
            Storyboard.SetTarget(fadeIn, PlayStateGlyphHost);
            Storyboard.SetTargetProperty(fadeIn, new PropertyPath(OpacityProperty));
            sb.Children.Add(fadeIn);

            // Scale Host: 0.6 → 1.0 за 180ms
            var scaleX = new DoubleAnimation(0.6, 1.0, TimeSpan.FromMilliseconds(180))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(scaleX, PlayStateScale);
            Storyboard.SetTargetProperty(scaleX, new PropertyPath(ScaleTransform.ScaleXProperty));
            sb.Children.Add(scaleX);

            var scaleY = scaleX.Clone();
            Storyboard.SetTarget(scaleY, PlayStateScale);
            Storyboard.SetTargetProperty(scaleY, new PropertyPath(ScaleTransform.ScaleYProperty));
            sb.Children.Add(scaleY);

            // Удержание 450ms, затем Opacity: 1 → 0 за 250ms
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250))
                { BeginTime = TimeSpan.FromMilliseconds(450) };
            Storyboard.SetTarget(fadeOut, PlayStateGlyphHost);
            Storyboard.SetTargetProperty(fadeOut, new PropertyPath(OpacityProperty));
            sb.Children.Add(fadeOut);

            // Скрыть Host после анимации
            var hideVis = new ObjectAnimationUsingKeyFrames();
            Storyboard.SetTarget(hideVis, PlayStateGlyphHost);
            Storyboard.SetTargetProperty(hideVis, new PropertyPath(VisibilityProperty));
            hideVis.KeyFrames.Add(new DiscreteObjectKeyFrame(
                Visibility.Collapsed,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700))));
            sb.Children.Add(hideVis);

            sb.Begin();
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ОБНОВЛЕНИЕ UI
        // ════════════════════════════════════════════════════════════════════════

        private void UpdateTimeLabel(TimeSpan current)
        {
            var total = Media.NaturalDuration.HasTimeSpan
                ? Media.NaturalDuration.TimeSpan
                : TimeSpan.Zero;

            TbTime.Text = $"{FormatTime(current)} / {FormatTime(total)}";
        }

        private void UpdateChapterLabel(TimeSpan current)
        {
            ITimecode? active = null;
            foreach (var tc in _timecodes)
            {
                if (tc.Position <= current) active = tc;
                else break;
            }

            if (active != null)
            {
                TbChapter.Text    = active.Label;
                TbChapter.Opacity = 1.0;
            }
            else
            {
                TbChapter.Opacity = 0.0;
            }
        }

        private void UpdateTimelineValue(TimeSpan current)
        {
            if (_isScrubbing || !Media.NaturalDuration.HasTimeSpan) return;

            var total = Media.NaturalDuration.TimeSpan;
            if (total.TotalSeconds <= 0) return;

            Timeline.Value = current.TotalSeconds / total.TotalSeconds;
        }

        /// <summary>
        /// Обновляет иконку громкости по порогам.
        /// E992 = Mute; E993 = тихо; E994 = средне; E995 = громко.
        /// </summary>
        private void UpdateVolumeGlyph()
        {
            double vol = IsMuted ? 0.0 : Volume;

            GlyphVolume.Text = vol switch
            {
                0.0     => "\uE992",  // Mute 
                <= 0.33 => "\uE993",  // Volume1
                <= 0.66 => "\uE994",  // Volume2
                _       => "\uE995",  // Volume3 (max)
            };
        }

        private static string FormatTime(TimeSpan ts) =>
            ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes}:{ts.Seconds:D2}";

        private void TogglePlayPause()
        {
            if (IsLocked) return;
            PlayerState = PlayerState == PlayerState.Playing
                ? PlayerState.Paused
                : PlayerState.Playing;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ОБРАБОТЧИКИ КНОПОК
        // ════════════════════════════════════════════════════════════════════════

        private void BtnPlayPause_Click(object sender, RoutedEventArgs e) => TogglePlayPause();

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
            => RaiseEvent(new RoutedEventArgs(PreviousRequestedEvent));

        private void BtnNext_Click(object sender, RoutedEventArgs e)
            => RaiseEvent(new RoutedEventArgs(NextRequestedEvent));

        private void BtnMute_Click(object sender, RoutedEventArgs e)
        {
            double oldVol = IsMuted ? 0 : Volume;

            IsMuted = !IsMuted;

            double newVol = IsMuted ? 0 : Volume;
            RaiseEvent(new VolumeChangedEventArgs(VolumeChangedEvent, oldVol, newVol));
        }

        private void BtnFullscreen_Click(object sender, RoutedEventArgs e)
            => VideoWindowState = VideoWindowState.Maximized;

        private void BtnPip_Click(object sender, RoutedEventArgs e)
            => VideoWindowState = VideoWindowState.PiP;

        private void BtnNormal_Click(object sender, RoutedEventArgs e)
            => VideoWindowState = VideoWindowState.Normal;

        private void BtnSpeed_Click(object sender, RoutedEventArgs e)
        {
            if (BtnSpeed.ContextMenu is { } menu)
            {
                menu.PlacementTarget = BtnSpeed;
                menu.Placement       = System.Windows.Controls.Primitives.PlacementMode.Top;
                menu.IsOpen          = true;
            }
        }

        private void SpeedItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item
                && item.Tag is string tag
                && double.TryParse(tag, NumberStyles.Any, CultureInfo.InvariantCulture, out double speed))
            {
                _playbackSpeed   = speed;
                TbSpeed.Text     = speed == 1.0 ? "1×" : $"{speed}×";
                Media.SpeedRatio = speed;

                foreach (MenuItem mi in SpeedMenu.Items)
                    mi.IsChecked = mi == item;
            }
        }

        // ── Таймлайн ─────────────────────────────────────────────────────────

        private void Timeline_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (!Media.NaturalDuration.HasTimeSpan) return;

            _isScrubbing = true;

            var pos = TimeSpan.FromSeconds(
                Media.NaturalDuration.TimeSpan.TotalSeconds * e.NewValue);

            Media.Position = pos;
            UpdateTimeLabel(pos);
            UpdateChapterLabel(pos);

            // Сбрасываем scrubbing с небольшой задержкой
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            t.Tick += (_, _) => { _isScrubbing = false; t.Stop(); };
            t.Start();
        }

        // ── Громкость ─────────────────────────────────────────────────────────

        private void VolumeBar_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            double oldVol = Volume;

            // Снимаем mute если потащили вверх
            if (IsMuted && e.NewValue > 0) IsMuted = false;

            Volume         = e.NewValue;
            Media.Volume   = IsMuted ? 0 : e.NewValue;

            UpdateVolumeGlyph();
            RaiseEvent(new VolumeChangedEventArgs(VolumeChangedEvent, oldVol, e.NewValue));
        }

        // ── Lock overlay: поглощаем события ──────────────────────────────────

        private void LockOverlay_PreviewMouseDown(object sender, MouseButtonEventArgs e)
            => e.Handled = true;

        private void LockOverlay_PreviewKeyDown(object sender, KeyEventArgs e)
            => e.Handled = true;

        // ════════════════════════════════════════════════════════════════════════
        //  REPARENTING — FULLSCREEN / PiP / NORMAL
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Сохраняет текущего родителя, извлекает SmotrelPlayer из него
        /// и помещает в новый контейнер.
        /// </summary>
        private void DetachFromParent()
        {
            if (Parent is Panel panel)
            {
                _originalChildIndex = panel.Children.IndexOf(this);
                _originalParent     = panel;
                panel.Children.Remove(this);
            }
            else if (Parent is ContentControl cc)
            {
                _originalParent = null;
                cc.Content      = null;
            }
            else if (Parent is Decorator dec)
            {
                _originalParent = null;
                dec.Child       = null;
            }
        }

        /// <summary>
        /// Возвращает SmotrelPlayer обратно в оригинальный родительский контейнер.
        /// </summary>
        private void ReattachToParent()
        {
            if (_originalParent != null)
            {
                _originalParent.Children.Insert(_originalChildIndex, this);
                _originalParent = null;
            }
        }

        private void EnterFullscreen()
        {
            DetachFromParent();

            _fullscreenWindow = new Window
            {
                WindowStyle       = WindowStyle.None,
                WindowState       = System.Windows.WindowState.Maximized,
                ResizeMode        = ResizeMode.NoResize,
                Background        = Brushes.Black,
                Topmost           = true,
                AllowsTransparency = false,
                Content           = this,
            };

            // При закрытии окна — возвращаемся в Normal
            _fullscreenWindow.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape || e.Key == Key.F11)
                    VideoWindowState = VideoWindowState.Normal;
            };

            _fullscreenWindow.Show();
        }

        private void EnterPiP()
        {
            DetachFromParent();

            _pipWindow = new Window
            {
                Width             = 400,
                Height            = 225,
                WindowStyle       = WindowStyle.None,
                ResizeMode        = ResizeMode.CanResize,
                Background        = Brushes.Black,
                Topmost           = true,
                AllowsTransparency = false,
                Title             = "SmotrelPlayer PiP",
                Content           = this,
            };

            _pipWindow.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                    VideoWindowState = VideoWindowState.Normal;
            };

            _pipWindow.Show();
        }

        private void ExitToNormal()
        {
            // Закрываем вспомогательные окна не вызывая повторного события
            if (_fullscreenWindow != null)
            {
                _fullscreenWindow.Content = null;
                _fullscreenWindow.Close();
                _fullscreenWindow = null;
            }

            if (_pipWindow != null)
            {
                _pipWindow.Content = null;
                _pipWindow.Close();
                _pipWindow = null;
            }

            ReattachToParent();
        }
    }
}
