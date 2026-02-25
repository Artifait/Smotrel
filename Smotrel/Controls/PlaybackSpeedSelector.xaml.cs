using System.Windows;
using System.Windows.Controls;

namespace Smotrel.Controls
{
    public partial class PlaybackSpeedSelector : UserControl
    {
        public PlaybackSpeedSelector()
        {
            InitializeComponent();
        }

        public double PlaybackSpeed
        {
            get => (double)GetValue(PlaybackSpeedProperty);
            set => SetValue(PlaybackSpeedProperty, value);
        }

        public static readonly DependencyProperty PlaybackSpeedProperty =
            DependencyProperty.Register(nameof(PlaybackSpeed), typeof(double), typeof(PlaybackSpeedSelector),
                new PropertyMetadata(1.0, OnPlaybackSpeedChanged));

        private static void OnPlaybackSpeedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PlaybackSpeedSelector selector)
                selector.PlaybackSpeedChanged?.Invoke(selector, (double)e.NewValue);
        }

        public double[] AvailableSpeeds
        {
            get => (double[])GetValue(AvailableSpeedsProperty);
            set => SetValue(AvailableSpeedsProperty, value);
        }

        public static readonly DependencyProperty AvailableSpeedsProperty =
            DependencyProperty.Register(nameof(AvailableSpeeds), typeof(double[]), typeof(PlaybackSpeedSelector),
                new PropertyMetadata(new double[] { 0.25, 0.5, 1.0, 1.25, 1.5, 2.0 }));

        public event PlaybackSpeedChangedEventHandler PlaybackSpeedChanged;

        private void SpeedButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && double.TryParse(btn.Content.ToString().Replace("x", ""), out double speed))
            {
                PlaybackSpeed = speed;
                MainButton.IsChecked = false;
            }
        }
    }

    public delegate void PlaybackSpeedChangedEventHandler(object sender, double newSpeed);
}
