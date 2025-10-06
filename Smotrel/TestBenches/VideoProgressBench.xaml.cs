using System.Windows;
using System.Windows.Threading;

namespace Smotrel.TestBenches
{
    public partial class VideoProgressBench : Window
    {
        private readonly DispatcherTimer _demoTimer;

        public VideoProgressBench()
        {
            InitializeComponent();

            VolumeCtrl.VolumeChanged += (s, volume) =>
            {
                ProgressCtrl.Value = volume * ProgressCtrl.Maximum;
                DebugText.Text = $"Volume: {(int)(volume * 100)}% → Video position: {ProgressCtrl.Value:F2}";
            };

            ProgressCtrl.SeekRequested += (s, value) =>
            {
                VolumeCtrl.Volume = value / ProgressCtrl.Maximum;
                DebugText.Text = $"Seek to {value:F2} → Volume updated to {(int)(VolumeCtrl.Volume * 100)}%";
            };

            _demoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _demoTimer.Tick += (_, _) =>
            {
                ProgressCtrl.Value += 0.01;
                if (ProgressCtrl.Value >= ProgressCtrl.Maximum)
                    ProgressCtrl.Value = VolumeCtrl.Volume * ProgressCtrl.Maximum;
            };
            _demoTimer.Start();
        }
    }
}
