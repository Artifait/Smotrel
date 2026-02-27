using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Smotrel.Controls
{
    /// <summary>
    /// Кастомный горизонтальный бар громкости с поддержкой перехода в произвольную
    /// позицию по одиночному клику или перетаскиванию.
    ///
    /// Логика иконки динамика находится вне этого контрола — в родительском
    /// шаблоне. Здесь только бар + событие изменения значения.
    ///
    /// Визуальные слои (рисуются в OnRender):
    ///   1. Трек     — фон (TrackBrush)
    ///   2. Заливка  — текущий уровень (ProgressBrush)
    ///   3. Thumb    — кружок на текущей позиции (при ховере)
    /// </summary>
    public class ClickableVolumeBar : FrameworkElement
    {
        // ── Константы ────────────────────────────────────────────────────────────

        private const double HeightNormal   = 4.0;
        private const double HeightHovered  = 5.0;
        private const double ThumbRadiusHovered = 5.0;

        // ── Состояние ────────────────────────────────────────────────────────────

        private bool   _isDragging;
        private double _thumbRadius = 0;

        // ── Конструктор ──────────────────────────────────────────────────────────

        public ClickableVolumeBar()
        {
            Height = HeightNormal;
            Cursor = Cursors.Hand;
        }

        // ── Dependency Properties ────────────────────────────────────────────────

        /// <summary>Текущий уровень громкости (0.0 — 1.0).</summary>
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(
                nameof(Value), typeof(double), typeof(ClickableVolumeBar),
                new FrameworkPropertyMetadata(
                    1.0,
                    FrameworkPropertyMetadataOptions.AffectsRender,
                    null,
                    CoerceNormalizedValue));

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        /// <summary>Цвет заливки (текущий уровень).</summary>
        public static readonly DependencyProperty ProgressBrushProperty =
            DependencyProperty.Register(
                nameof(ProgressBrush), typeof(Brush), typeof(ClickableVolumeBar),
                new FrameworkPropertyMetadata(
                    Brushes.White,
                    FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush ProgressBrush
        {
            get => (Brush)GetValue(ProgressBrushProperty);
            set => SetValue(ProgressBrushProperty, value);
        }

        /// <summary>Цвет трека (фон).</summary>
        public static readonly DependencyProperty TrackBrushProperty =
            DependencyProperty.Register(
                nameof(TrackBrush), typeof(Brush), typeof(ClickableVolumeBar),
                new FrameworkPropertyMetadata(
                    new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                    FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush TrackBrush
        {
            get => (Brush)GetValue(TrackBrushProperty);
            set => SetValue(TrackBrushProperty, value);
        }

        // ── Routed Events ────────────────────────────────────────────────────────

        /// <summary>
        /// Вызывается при изменении Value пользователем.
        /// Стратегия: Bubble — всплывает до Window.
        /// </summary>
        public static readonly RoutedEvent ValueChangedEvent =
            EventManager.RegisterRoutedEvent(
                nameof(ValueChanged),
                RoutingStrategy.Bubble,
                typeof(RoutedPropertyChangedEventHandler<double>),
                typeof(ClickableVolumeBar));

        public event RoutedPropertyChangedEventHandler<double> ValueChanged
        {
            add    => AddHandler(ValueChangedEvent, value);
            remove => RemoveHandler(ValueChangedEvent, value);
        }

        // ── Coerce ───────────────────────────────────────────────────────────────

        private static object CoerceNormalizedValue(DependencyObject d, object baseValue)
        {
            double v = (double)baseValue;
            return Math.Max(0.0, Math.Min(1.0, v));
        }

        // ── Mouse — логика клика-в-позицию ───────────────────────────────────────

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

            _isDragging = true;
            CaptureMouse();

            SeekToMousePosition(e);
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
                SeekToMousePosition(e);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);

            if (_isDragging)
            {
                _isDragging = false;
                ReleaseMouseCapture();
            }
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            AnimateHeight(HeightHovered);
            _thumbRadius = ThumbRadiusHovered;
            InvalidateVisual();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            AnimateHeight(HeightNormal);
            _thumbRadius = 0;
            InvalidateVisual();
        }

        /// <summary>
        /// Вычисляет нормализованное значение из координаты X мыши,
        /// обновляет Value и поднимает ValueChangedEvent.
        /// </summary>
        private void SeekToMousePosition(MouseEventArgs e)
        {
            if (ActualWidth <= 0) return;

            double x        = Math.Max(0, Math.Min(ActualWidth, e.GetPosition(this).X));
            double newValue = x / ActualWidth;
            double oldValue = Value;

            Value = newValue;

            if (Math.Abs(oldValue - newValue) > double.Epsilon)
            {
                RaiseEvent(new RoutedPropertyChangedEventArgs<double>(
                    oldValue, Value, ValueChangedEvent));
            }
        }

        /// <summary>Плавная анимация высоты (ховер/уход).</summary>
        private void AnimateHeight(double targetHeight)
        {
            var anim = new DoubleAnimation(targetHeight, new Duration(TimeSpan.FromMilliseconds(120)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(HeightProperty, anim);
        }

        // ── Render ───────────────────────────────────────────────────────────────

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;

            if (w <= 0 || h <= 0) return;

            double r = h / 2;

            // 1. Трек (фон)
            dc.DrawRoundedRectangle(TrackBrush, null, new Rect(0, 0, w, h), r, r);

            // 2. Заливка (текущий уровень громкости)
            double fillW = w * Math.Max(0, Math.Min(1, Value));
            if (fillW > r * 2)
                dc.DrawRoundedRectangle(ProgressBrush, null, new Rect(0, 0, fillW, h), r, r);

            // 3. Thumb (виден только при ховере)
            if (_thumbRadius > 0)
            {
                double thumbX = Math.Max(_thumbRadius, Math.Min(w - _thumbRadius, fillW));

                dc.DrawEllipse(
                    ProgressBrush,
                    null,
                    new Point(thumbX, h / 2),
                    _thumbRadius,
                    _thumbRadius);
            }
        }
    }
}
