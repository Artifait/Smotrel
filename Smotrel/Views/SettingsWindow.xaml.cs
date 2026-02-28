using Microsoft.Win32;
using Smotrel.Settings;
using System.Windows;
using System.Windows.Input;

namespace Smotrel.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            LoadValues();
        }

        private void LoadValues()
        {
            var s = AppSettings.Current;
            TbCoursesFolder.Text  = s.CoursesFolder;
            TbOverlayTimeout.Text = s.OverlayTimeoutSeconds.ToString();
            TbSeekBack.Text       = s.SeekBackwardSeconds.ToString();
            TbSeekFwd.Text        = s.SeekForwardSeconds.ToString();
            HkPlayPause.Text      = s.HotkeyPlayPause;
            HkFullscreen.Text     = s.HotkeyFullscreen;
            HkPiP.Text            = s.HotkeyPiP;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var s = AppSettings.Current;
            s.CoursesFolder = TbCoursesFolder.Text.Trim();

            if (int.TryParse(TbOverlayTimeout.Text, out int ot) && ot > 0)
                s.OverlayTimeoutSeconds = ot;
            if (int.TryParse(TbSeekBack.Text, out int sb) && sb > 0)
                s.SeekBackwardSeconds = sb;
            if (int.TryParse(TbSeekFwd.Text, out int sf) && sf > 0)
                s.SeekForwardSeconds = sf;

            s.HotkeyPlayPause  = HkPlayPause.Text.Trim();
            s.HotkeyFullscreen = HkFullscreen.Text.Trim();
            s.HotkeyPiP        = HkPiP.Text.Trim();

            s.Save();
            AppSettings.Reload();

            MessageBox.Show(this, "Настройки сохранены.", "Готово",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }

        // ── Захват горячей клавиши ────────────────────────────────────────────

        private void Hotkey_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;

            var key  = e.Key == Key.System ? e.SystemKey : e.Key;
            var mods = Keyboard.Modifiers;

            if (key == Key.LeftShift || key == Key.RightShift
                || key == Key.LeftCtrl || key == Key.RightCtrl
                || key == Key.LeftAlt  || key == Key.RightAlt)
                return; // только модификатор — ждём основную клавишу

            string result = mods switch
            {
                ModifierKeys.Shift   => "Shift+",
                ModifierKeys.Control => "Ctrl+",
                ModifierKeys.Alt     => "Alt+",
                _                    => string.Empty,
            } + key.ToString();

            if (sender is System.Windows.Controls.TextBox tb)
                tb.Text = result;
        }

        // ── Папка с курсами ───────────────────────────────────────────────────

        private void BrowseCoursesFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "Папка с курсами" };
            if (dlg.ShowDialog() == true)
                TbCoursesFolder.Text = dlg.FolderName;
        }

        // ── Окно ─────────────────────────────────────────────────────────────

        private void Header_Down(object sender, MouseButtonEventArgs e)
        {
            try { DragMove(); } catch { }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
