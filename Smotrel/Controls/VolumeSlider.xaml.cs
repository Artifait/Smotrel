using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Smotrel.Controls
{
    public partial class VolumeSlider : UserControl
    {
        public static readonly DependencyProperty VolumeProperty =
            DependencyProperty.Register(nameof(Volume), typeof(double), typeof(VolumeSlider),
                new PropertyMetadata(0.5, OnVolumeChanged));

        public double Volume
        {
            get => (double)GetValue(VolumeProperty);
            set => SetValue(VolumeProperty, Math.Max(0, Math.Min(1, value)));
        }

        public event EventHandler<double> VolumeChanged;

        private bool _dragging;

        public VolumeSlider()
        {
            InitializeComponent();
            Loaded += (_, _) => UpdateVisuals();
            SizeChanged += (_, _) => UpdateVisuals();
        }

        private static void OnVolumeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VolumeSlider control)
                control.UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            if (Fill == null || Root == null) return;
            double width = Root.ActualWidth * Math.Max(0, Math.Min(1, Volume));
            Fill.Width = width;

            if (Thumb != null)
            {
                double left = width - Thumb.Width / 2.0;
                left = Math.Max(-Thumb.Width / 2.0, Math.Min(left, Root.ActualWidth - Thumb.Width / 2.0));
                Canvas.SetLeft(Thumb, left);
                Canvas.SetTop(Thumb, (Root.ActualHeight - Thumb.Height) / 2.0);
            }
        }

        private void SetVolumeFromPosition(double x)
        {
            if (Root == null) return;
            double v = x / Root.ActualWidth;
            Volume = Math.Max(0, Math.Min(1, v));
            VolumeChanged?.Invoke(this, Volume);
            UpdateVisuals();
        }

        private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragging = true;
            Mouse.Capture(Root);
            SetVolumeFromPosition(e.GetPosition(Root).X);
        }

        private void Root_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            SetVolumeFromPosition(e.GetPosition(Root).X);
        }

        private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            Mouse.Capture(null);
        }
    }
}
