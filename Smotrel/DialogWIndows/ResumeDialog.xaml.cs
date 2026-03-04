using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace Smotrel.DialogWindows
{
    public partial class ResumeDialog : Window, INotifyPropertyChanged
    {
        // ── Результат ─────────────────────────────────────────────────────
        public bool ShouldResume { get; private set; } = true;

        // ── Состояние выбора (биндинг для обводки) ────────────────────────

        private bool _isResumeSelected = true;
        public bool IsResumeSelected
        {
            get => _isResumeSelected;
            private set { _isResumeSelected = value; Notify(); }
        }

        // ── Конструктор ───────────────────────────────────────────────────

        public ResumeDialog(string videoTitle, TimeSpan lastPosition)
        {
            DataContext = this;
            InitializeComponent();

            TbVideoTitle.Text = videoTitle;
            TbResumeTime.Text = FmtTime(lastPosition);
        }

        // ── Навигация клавишами ───────────────────────────────────────────

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Left:
                case Key.Up:
                    IsResumeSelected = true;
                    e.Handled = true;
                    break;

                case Key.Right:
                case Key.Down:
                    IsResumeSelected = false;
                    e.Handled = true;
                    break;

                case Key.Enter:
                    Confirm();
                    e.Handled = true;
                    break;

                case Key.Escape:
                    // Escape = начать сначала (безопасный дефолт)
                    IsResumeSelected = false;
                    Confirm();
                    e.Handled = true;
                    break;
            }
        }

        // ── Клики мышью ───────────────────────────────────────────────────

        private void BtnResume_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            IsResumeSelected = true;
            Confirm();
        }

        private void BtnRestart_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            IsResumeSelected = false;
            Confirm();
        }

        private void Confirm()
        {
            ShouldResume = IsResumeSelected;
            DialogResult = true;
            Close();
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static string FmtTime(TimeSpan ts) =>
            ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes}:{ts.Seconds:D2}";

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string? p = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}