using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Smotrel.Controls
{
    public partial class VideoProgressSlider : UserControl
    {
        public event EventHandler<double> SeekRequested;
        public event EventHandler<double> ValueChanging;
        public event EventHandler DragStarted;
        public event EventHandler DragCompleted;
        private bool _isDragging;

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(VideoProgressSlider),
                new PropertyMetadata(0.0, OnValueChanged));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(VideoProgressSlider),
                new PropertyMetadata(1.0));

        public static readonly DependencyProperty BufferValueProperty =
            DependencyProperty.Register(nameof(BufferValue), typeof(double), typeof(VideoProgressSlider),
                new PropertyMetadata(0.0, OnBufferChanged));

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public double Maximum
        {
            get => (double)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public double BufferValue
        {
            get => (double)GetValue(BufferValueProperty);
            set => SetValue(BufferValueProperty, value);
        }

        public bool IsDragging => _isDragging;

        public VideoProgressSlider()
        {
            InitializeComponent();
            Loaded += (_, _) => { UpdateVisuals(); UpdateBuffer(); };
            SizeChanged += (_, _) => { UpdateVisuals(); UpdateBuffer(); };
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VideoProgressSlider control && !control._isDragging)
                control.UpdateVisuals();
        }

        private static void OnBufferChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VideoProgressSlider control)
                control.UpdateBuffer();
        }

        private static double Clamp(double v, double a, double b) => Math.Max(a, Math.Min(b, v));

        private void UpdateBuffer()
        {
            if (BufferTrack == null || RootGrid == null || Maximum <= 0) return;
            double ratio = Maximum <= 0 ? 0 : Clamp(BufferValue / Maximum, 0, 1);
            BufferTrack.Width = ratio * RootGrid.ActualWidth;
        }

        private void UpdateVisuals()
        {
            if (ProgressTrack == null || RootGrid == null || Maximum <= 0) return;
            double ratio = Maximum <= 0 ? 0 : Clamp(Value / Maximum, 0, 1);
            double w = ratio * RootGrid.ActualWidth;
            ProgressTrack.Width = w;
            // позиционирование thumb — в пределах canvas
            if (Thumb != null)
            {
                double left = w - (Thumb.Width / 2.0);
                left = Clamp(left, -Thumb.Width / 2.0, RootGrid.ActualWidth - Thumb.Width / 2.0);
                Canvas.SetLeft(Thumb, left);
                // вертикальная позиция чтобы совпадал центр
                double top = (RootGrid.ActualHeight - Thumb.Height) / 2.0;
                Canvas.SetTop(Thumb, top);
            }
        }

        private void UpdateValueFromPosition(double x)
        {
            if (RootGrid == null || Maximum <= 0) return;
            double ratio = Clamp(x / RootGrid.ActualWidth, 0, 1);
            Value = ratio * Maximum;
            UpdateVisuals();
        }

        private void RootGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            Mouse.Capture(RootGrid);
            DragStarted?.Invoke(this, EventArgs.Empty);

            var pos = e.GetPosition(RootGrid);
            UpdateValueFromPosition(pos.X);
            e.Handled = true;
        }

        private void RootGrid_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;

            var pos = e.GetPosition(RootGrid);
            UpdateValueFromPosition(pos.X);

            ValueChanging?.Invoke(this, Value);
        }

        private void RootGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;
            _isDragging = false;
            Mouse.Capture(null);
            DragCompleted?.Invoke(this, EventArgs.Empty);
            SeekRequested?.Invoke(this, Value);
        }
    }
}
