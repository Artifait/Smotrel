using System.Windows;
using System.Windows.Threading;

namespace Smotrel.TestBenches
{
    public partial class VideoControlsTestBench : Window
    {
        private DispatcherTimer _timer;
        private TimeSpan _videoLength = TimeSpan.FromMinutes(26);
        private TimeSpan _currentTime = TimeSpan.Zero;


        public VideoControlsTestBench()
        {
            InitializeComponent();

            ProgressSlider.Maximum = _videoLength.TotalSeconds;
            TimeDisplay.TotalTime = _videoLength;

            ProgressSlider.ValueChanging += (_, val) =>
            {    
                TimeDisplay.CurrentTime = TimeSpan.FromSeconds(val);
            };
            ProgressSlider.SeekRequested += (_, val) =>
            {
                _currentTime = TimeSpan.FromSeconds(val);
                TimeDisplay.CurrentTime = _currentTime;
            };

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.1) };
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (!ProgressSlider.IsDragging)
            {
                _currentTime += TimeSpan.FromSeconds(0.1 * SpeedSelector.PlaybackSpeed);
                if (_currentTime > _videoLength)
                    _currentTime = _videoLength;

                ProgressSlider.Value = _currentTime.TotalSeconds;
                TimeDisplay.CurrentTime = _currentTime;
            }
        }
    }
}
