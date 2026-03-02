using System.Windows;
using System.Windows.Input;

namespace Smotrel.DialogWindows
{
    public partial class AddTimecodeDialog : Window
    {
        public TimeSpan ResultPosition { get; private set; }
        public string   ResultLabel    { get; private set; } = string.Empty;

        private readonly TimeSpan _playbackPos;

        public AddTimecodeDialog(TimeSpan playbackPos)
        {
            InitializeComponent();
            _playbackPos = playbackPos;
            TbPos.Text   = FmtTime(playbackPos);
            Loaded       += (_, _) => { TbLabel.Focus(); };
        }

        private void UseCurrentTime_Click(object sender, RoutedEventArgs e)
        {
            TbPos.Text       = FmtTime(_playbackPos);
            TbError.Visibility = Visibility.Collapsed;
        }

        private void Add_Click(object sender, RoutedEventArgs e)  => TryCommit();
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TbPos_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)  { e.Handled = true; TbLabel.Focus(); }
            if (e.Key == Key.Escape) { DialogResult = false; Close(); }
        }

        private void TbLabel_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)  { e.Handled = true; TryCommit(); }
            if (e.Key == Key.Escape) { DialogResult = false; Close(); }
        }

        private void TryCommit()
        {
            if (!TryParse(TbPos.Text.Trim(), out var pos))
            {
                TbError.Visibility = Visibility.Visible;
                TbPos.Focus(); TbPos.SelectAll();
                return;
            }

            TbError.Visibility = Visibility.Collapsed;
            ResultPosition     = pos;
            ResultLabel        = string.IsNullOrWhiteSpace(TbLabel.Text)
                ? FmtTime(pos)
                : TbLabel.Text.Trim();

            DialogResult = true;
            Close();
        }

        private static bool TryParse(string s, out TimeSpan result)
        {
            result = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(s)) return false;

            var p = s.Split(':');
            try
            {
                result = p.Length switch
                {
                    1 => TimeSpan.FromSeconds(int.Parse(p[0])),
                    2 => new TimeSpan(0, int.Parse(p[0]), int.Parse(p[1])),
                    3 => new TimeSpan(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2])),
                    _ => throw new FormatException()
                };
                return true;
            }
            catch { return false; }
        }

        private static string FmtTime(TimeSpan ts) =>
            ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes}:{ts.Seconds:D2}";

        private void Header_Down(object sender, MouseButtonEventArgs e)
        {
            try { DragMove(); } catch { }
        }
    }
}
