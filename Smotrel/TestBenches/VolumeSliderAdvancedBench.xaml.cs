using System.Windows;

namespace Smotrel.TestBenches
{
    public partial class VolumeSliderAdvancedBench : Window
    {
        public VolumeSliderAdvancedBench()
        {
            InitializeComponent();
            VolumeAdvanced.VolumeChanged += (_, value) =>
            {
                VolumeLabel.Text = $"Volume: {(int)(value * 100)}%";
            };
        }
    }
}
