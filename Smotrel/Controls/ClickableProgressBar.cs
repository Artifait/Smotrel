using Smotrel.Interfaces;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Smotrel.Controls
{
    public class ClickableProgressBar : FrameworkElement
    {
        private const double HeightNormal = 4.0;
        private const double HeightHovered =  8.0;
        private const double ThumbRadiusNormal = 0.0;
        private const double ThumbRadiusHovered = 6;
        private const double MarkerHitThreshold = 10.0;

        private bool _isDragging;
        private double _thumbRadius = ThumbRadiusNormal;
        private double _pendingValue;
        private Popup _timecodePopup;
        private System.Windows.Controls.TextBlock _timecodeText;

        public ClickableProgressBar()
        {
            Height = HeightNormal;
            Cursor = Cursors.Hand;

            _timecodeText = new System.Windows.Controls.TextBlock
            {
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(200, 20, 20, 20)),
                Padding = new Thickness(8, 3, 8, 3),
                FontSize = 11,
                FontFamily = new FontFamily("Arial"),
            };

            _timecodePopup = new Popup
            {
                Child = _timecodeText,
                PlacementTarget = this,
                Placement = PlacementMode.Top,
                AllowsTransparency = true,
                IsOpen = false,
                VerticalOffset = -6,
            };

            _pendingValue = Value;
        }

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(
                nameof(Value), typeof(double), typeof(ClickableProgressBar),
                new FrameworkPropertyMetadata(
                    0.0,
                    FrameworkPropertyMetadataOptions.AffectsRender,
                    null,
                    CoerceNormalizedValue));

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public static readonly DependencyProperty BufferedValueProperty =
            DependencyProperty.Register(
                nameof(BufferedValue), typeof(double), typeof(ClickableProgressBar),
                new FrameworkPropertyMetadata(
                    0.0,
                    FrameworkPropertyMetadataOptions.AffectsRender,
                    null,
                    CoerceNormalizedValue));

        public double BufferedValue
        {
            get => (double)GetValue(BufferedValueProperty);
            set => SetValue(BufferedValueProperty, value);
        }

        public static readonly DependencyProperty DurationProperty =
            DependencyProperty.Register(
                nameof(Duration), typeof(TimeSpan), typeof(ClickableProgressBar),
                new FrameworkPropertyMetadata(
                    TimeSpan.Zero,
                    FrameworkPropertyMetadataOptions.AffectsRender));

        public TimeSpan Duration
        {
            get => (TimeSpan)GetValue(DurationProperty);
            set => SetValue(DurationProperty, value);
        }

        public static readonly DependencyProperty TimecodesProperty =
            DependencyProperty.Register(
                nameof(Timecodes), typeof(IList<ITimecode>), typeof(ClickableProgressBar),
                new FrameworkPropertyMetadata(
                    null,
                    FrameworkPropertyMetadataOptions.AffectsRender));

        public IList<ITimecode> Timecodes
        {
            get => (IList<ITimecode>)GetValue(TimecodesProperty);
            set => SetValue(TimecodesProperty, value);
        }

        public static readonly DependencyProperty ProgressBrushProperty =
            DependencyProperty.Register(
                nameof(ProgressBrush), typeof(Brush), typeof(ClickableProgressBar),
                new FrameworkPropertyMetadata(
                    Brushes.DodgerBlue,
                    FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush ProgressBrush
        {
            get => (Brush)GetValue(ProgressBrushProperty);
            set => SetValue(ProgressBrushProperty, value);
        }

        public static readonly DependencyProperty BufferBrushProperty =
            DependencyProperty.Register(
                nameof(BufferBrush), typeof(Brush), typeof(ClickableProgressBar),
                new FrameworkPropertyMetadata(
                    new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                    FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush BufferBrush
        {
            get => (Brush)GetValue(BufferBrushProperty);
            set => SetValue(BufferBrushProperty, value);
        }

        public static readonly DependencyProperty TrackBrushProperty =
            DependencyProperty.Register(
                nameof(TrackBrush), typeof(Brush), typeof(ClickableProgressBar),
                new FrameworkPropertyMetadata(
                    new SolidColorBrush(Color.FromRgb(55, 55, 55)),
                    FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush TrackBrush
        {
            get => (Brush)GetValue(TrackBrushProperty);
            set => SetValue(TrackBrushProperty, value);
        }

        public static readonly RoutedEvent ValueChangedEvent =
            EventManager.RegisterRoutedEvent(
                nameof(ValueChanged),
                RoutingStrategy.Bubble,
                typeof(RoutedPropertyChangedEventHandler<double>),
                typeof(ClickableProgressBar));

        public event RoutedPropertyChangedEventHandler<double> ValueChanged
        {
            add => AddHandler(ValueChangedEvent, value);
            remove => RemoveHandler(ValueChangedEvent, value);
        }

        public static readonly RoutedEvent SeekStartedEvent =
            EventManager.RegisterRoutedEvent(
                nameof(SeekStarted),
                RoutingStrategy.Bubble,
                typeof(RoutedPropertyChangedEventHandler<double>),
                typeof(ClickableProgressBar));

        public event RoutedPropertyChangedEventHandler<double> SeekStarted
        {
            add => AddHandler(SeekStartedEvent, value);
            remove => RemoveHandler(SeekStartedEvent, value);
        }

        public static readonly RoutedEvent SeekCompletedEvent =
            EventManager.RegisterRoutedEvent(
                nameof(SeekCompleted),
                RoutingStrategy.Bubble,
                typeof(RoutedPropertyChangedEventHandler<double>),
                typeof(ClickableProgressBar));

        public event RoutedPropertyChangedEventHandler<double> SeekCompleted
        {
            add => AddHandler(SeekCompletedEvent, value);
            remove => RemoveHandler(SeekCompletedEvent, value);
        }

        private static object CoerceNormalizedValue(DependencyObject d, object baseValue)
        {
            double v = (double)baseValue;
            return Math.Max(0.0, Math.Min(1.0, v));
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

            CaptureMouse();
            _isDragging = true;

            UpdatePendingFromMouse(e);
            RaiseEvent(new RoutedPropertyChangedEventArgs<double>(Value, _pendingValue, SeekStartedEvent));

            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                UpdatePendingFromMouse(e);
            }

            UpdateTimecodePopup(e);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);

            if (_isDragging)
            {
                _isDragging = false;
                double oldValue = Value;
                UpdatePendingFromMouse(e);
                ReleaseMouseCapture();

                Value = _pendingValue;

                if (Math.Abs(oldValue - Value) > double.Epsilon)
                {
                    RaiseEvent(new RoutedPropertyChangedEventArgs<double>(oldValue, Value, ValueChangedEvent));
                }

                RaiseEvent(new RoutedPropertyChangedEventArgs<double>(oldValue, Value, SeekCompletedEvent));
                InvalidateVisual();
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
            _timecodePopup.IsOpen = false;
            AnimateHeight(HeightNormal);
            _thumbRadius = ThumbRadiusNormal;
            InvalidateVisual();
        }

        private void UpdatePendingFromMouse(MouseEventArgs e)
        {
            if (ActualWidth <= 0) return;

            double x = Math.Max(0, Math.Min(ActualWidth, e.GetPosition(this).X));
            double newValue = x / ActualWidth;
            _pendingValue = Math.Max(0.0, Math.Min(1.0, newValue));
            InvalidateVisual();
        }

        private void UpdateTimecodePopup(MouseEventArgs e)
        {
            if (Timecodes == null || Timecodes.Count == 0 || Duration == TimeSpan.Zero)
            {
                _timecodePopup.IsOpen = false;
                return;
            }

            double mouseX = e.GetPosition(this).X;
            double nearestDist = double.MaxValue;
            ITimecode? nearest = null;

            foreach (var tc in Timecodes)
            {
                double markerX = (tc.Position.TotalSeconds / Duration.TotalSeconds) * ActualWidth;
                double dist = Math.Abs(mouseX - markerX);

                if (dist < MarkerHitThreshold && dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = tc;
                }
            }

            if (nearest != null)
            {
                _timecodeText.Text = nearest.Label;
                _timecodePopup.HorizontalOffset = e.GetPosition(this).X - 20;
                _timecodePopup.IsOpen = true;
            }
            else
            {
                _timecodePopup.IsOpen = false;
            }
        }

        private void AnimateHeight(double targetHeight)
        {
            var anim = new DoubleAnimation(targetHeight, new Duration(TimeSpan.FromMilliseconds(150)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(HeightProperty, anim);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;

            if (w <= 0 || h <= 0) return;

            double r = h / 2;

            dc.DrawRoundedRectangle(TrackBrush, null, new Rect(0, 0, w, h), r, r);

            double bufW = w * Math.Max(0, Math.Min(1, BufferedValue));
            if (bufW > r * 2)
                dc.DrawRoundedRectangle(BufferBrush, null, new Rect(0, 0, bufW, h), r, r);

            double displayValue = _isDragging ? _pendingValue : Value;
            double progressW = w * Math.Max(0, Math.Min(1, displayValue));
            if (progressW > r * 2)
                dc.DrawRoundedRectangle(ProgressBrush, null, new Rect(0, 0, progressW, h), r, r);

            if (Timecodes != null && Duration > TimeSpan.Zero)
            {
                var markerPen = new Pen(new SolidColorBrush(Color.FromArgb(180, 220, 220, 220)), 2);

                foreach (var tc in Timecodes)
                {
                    if (tc.Position <= TimeSpan.Zero) continue;

                    double markerX = (tc.Position.TotalSeconds / Duration.TotalSeconds) * w;
                    if (markerX <= 0 || markerX >= w) continue;

                    dc.DrawLine(markerPen,
                        new Point(markerX, 0),
                        new Point(markerX, h));
                }
            }

            if (_thumbRadius > 0)
            {
                double thumbX = progressW;
                thumbX = Math.Max(_thumbRadius, Math.Min(w - _thumbRadius, thumbX));

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