using Smotrel.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Smotrel.Views
{
    public partial class MainPlayer : Window
    {

        public CourseModel Course { get; private set; }
        public MainPlayer(CourseModel course)
        {
            InitializeComponent();
            Course = course;

            GapTitle.Text = "Smotrel - " + Course.Label;
            Loaded += OnWindowStateChanged;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = new MainWindow();
            mainWindow.Show();
            Close();
        }

        private void PipButton_Click(object sender, RoutedEventArgs e)
        {
            var pipWindow = new PipPlayer();

        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
            Application.Current.Shutdown();
        }

        private void Header_Down(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            var templateOfExitBtn = ExitBtn.Template;
            var templateOfSettingsBtn = BackBtn.Template;

            var rootBorderOfExitBtn = templateOfExitBtn.FindName("RootBorder", ExitBtn) as Border;
            var rootBorderOfBackBtn = templateOfSettingsBtn.FindName("RootBorder", BackBtn) as Border;

            if (WindowState == WindowState.Maximized)
            {
                ColumnOfWindowManagmentBtns.Width = new GridLength(120);

                exitBtnDamper.Width = new GridLength(5);
                backBtnDamper.Width = new GridLength(5);
                DamperGap.Height = new GridLength(5);

                if (rootBorderOfExitBtn != null)
                {
                    rootBorderOfExitBtn.CornerRadius = new CornerRadius(0);
                    rootBorderOfExitBtn.Width = 40;
                }

                if (rootBorderOfBackBtn != null)
                    rootBorderOfBackBtn.CornerRadius = new CornerRadius(0);
            }
            else
            {
                exitBtnDamper.Width = new GridLength(0);
                backBtnDamper.Width = new GridLength(0);
                DamperGap.Height = new GridLength(0);

                if (rootBorderOfExitBtn != null)
                    rootBorderOfExitBtn.CornerRadius = new CornerRadius(0, 10, 0, 0);

                if (rootBorderOfBackBtn != null)
                    rootBorderOfBackBtn.CornerRadius = new CornerRadius(10, 0, 0, 0);
            }
        }
    }
}
