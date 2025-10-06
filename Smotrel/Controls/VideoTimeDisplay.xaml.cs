using System.Windows;
using System.Windows.Controls;

namespace Smotrel.Controls
{
    public partial class VideoTimeDisplay : UserControl
    {
        public VideoTimeDisplay()
        {
            InitializeComponent();
            UpdateText();
        }

        public TimeSpan CurrentTime
        {
            get => (TimeSpan)GetValue(CurrentTimeProperty);
            set => SetValue(CurrentTimeProperty, value);
        }

        public static readonly DependencyProperty CurrentTimeProperty =
            DependencyProperty.Register(nameof(CurrentTime), typeof(TimeSpan), typeof(VideoTimeDisplay),
                new PropertyMetadata(TimeSpan.Zero, OnTimeChanged));

        public TimeSpan TotalTime
        {
            get => (TimeSpan)GetValue(TotalTimeProperty);
            set => SetValue(TotalTimeProperty, value);
        }

        public static readonly DependencyProperty TotalTimeProperty =
            DependencyProperty.Register(nameof(TotalTime), typeof(TimeSpan), typeof(VideoTimeDisplay),
                new PropertyMetadata(TimeSpan.Zero, OnTimeChanged));

        // Делегат для кастомного форматирования времени
        public Func<TimeSpan, TimeSpan, string> TimeFormatter
        {
            get => (Func<TimeSpan, TimeSpan, string>)GetValue(TimeFormatterProperty);
            set => SetValue(TimeFormatterProperty, value);
        }

        public static readonly DependencyProperty TimeFormatterProperty =
            DependencyProperty.Register(nameof(TimeFormatter), typeof(Func<TimeSpan, TimeSpan, string>),
                typeof(VideoTimeDisplay), new PropertyMetadata(null, OnTimeChanged));

        private static void OnTimeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VideoTimeDisplay display)
            {
                display.UpdateText();
            }
        }

        private void UpdateText()
        {
            if (TimeFormatter != null)
            {
                TimeText.Text = TimeFormatter(CurrentTime, TotalTime);
            }
            else
            {
                TimeText.Text = $"{FormatTime(CurrentTime)} / {FormatTime(TotalTime)}";
            }
        }

        // Базовое форматирование
        private string FormatTime(TimeSpan time)
        {
            if (time.TotalHours >= 1)
                return time.ToString(@"h\:mm\:ss");
            else
                return time.ToString(@"mm\:ss");
        }
    }
}
