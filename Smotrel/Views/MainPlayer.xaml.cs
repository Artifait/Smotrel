using Smotrel.Enums;
using Smotrel.Events;
using Smotrel.Interfaces;
using Smotrel.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Smotrel.Views
{
    public partial class MainPlayer : Window
    {
        // ── Публичные свойства ────────────────────────────────────────────────

        public CourseModel Course { get; private set; }

        // ── Текущее видео ─────────────────────────────────────────────────────

        private VideoModel? _currentVideo;

        // ── Конструктор ───────────────────────────────────────────────────────

        public MainPlayer(CourseModel course)
        {
            InitializeComponent();

            Course = course;
            GapTitle.Text = "Smotrel - " + Course.Label;

            // OnWindowStateChanged нужен и при Loaded (для первоначального выравнивания)
            Loaded += OnWindowStateChanged;
            Loaded += OnLoaded;
        }

        // ── Инициализация при загрузке ────────────────────────────────────────

        private void OnLoaded(object sender, EventArgs e)
        {
            _currentVideo = Course.GetVideoByAbsoluteIndex(0);
            if (_currentVideo == null) return;

            _currentVideo.Duration = TimeSpan.FromMinutes(50);
            LoadVideoIntoPlayer(_currentVideo);
        }

        // ── Загрузка видео в плеер ────────────────────────────────────────────

        /// <summary>
        /// Генерирует или берёт существующие таймкоды и передаёт видео в SmotrelPlayer.
        /// </summary>
        private void LoadVideoIntoPlayer(VideoModel video)
        {
            _currentVideo = video;

            var timecodes = BuildTimecodes(video);

            // LoadVideo — единая точка входа, задаёт все DP плеера
            Player.LoadVideo(video, timecodes);
        }

        /// <summary>
        /// Если у видео есть сохранённые таймкоды — берём их.
        /// Иначе генерируем 4 равномерных маркера через каждые 20% длины.
        /// </summary>
        private static IList<ITimecode> BuildTimecodes(VideoModel video)
        {
            if (video.Timestamps.Count > 0)
                return video.Timestamps.Cast<ITimecode>().ToList();

            if (video.Duration == TimeSpan.Zero)
                return new List<ITimecode>();

            string[] labels = { "Часть 1", "Часть 2", "Часть 3", "Часть 4" };
            var list = new List<ITimecode>();

            for (int i = 1; i <= 4; i++)
            {
                double fraction = i * 0.20;
                list.Add(VideoTimecode.Create(
                    TimeSpan.FromSeconds(video.Duration.TotalSeconds * fraction),
                    labels[i - 1]));
            }

            return list;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  СОБЫТИЯ SmotrelPlayer  (Bubble → перехватываем здесь)
        // ════════════════════════════════════════════════════════════════════════

        private void Player_PlaybackStateChanged(
            object sender,
            PlayerStateChangedEventArgs e)
        {
            // Здесь можно обновлять внешний UI, статистику и т.д.
            // Например, сохранять прогресс при паузе:
            // if (e.NewState == PlayerState.Paused && _currentVideo != null)
            //     _courseService.SaveProgress(_currentVideo, Player.CurrentTime);
        }

        private void Player_PlaybackEnded(object sender, RoutedEventArgs e)
        {
            // Видео закончилось — автоматически переходим к следующему
            if (_currentVideo == null) return;

            var next = Course.GetNextVideo(_currentVideo);
            if (next != null)
            {
                LoadVideoIntoPlayer(next);
            }
            else
            {
                // Это последнее видео курса
                Player.IsLocked = true;
                Player.LockMessage = "Курс завершён";
            }
        }

        private void Player_PreviousRequested(object sender, RoutedEventArgs e)
        {
            if (_currentVideo == null) return;

            var prev = Course.GetPreviousVideo(_currentVideo);
            if (prev != null)
                LoadVideoIntoPlayer(prev);
        }

        private void Player_NextRequested(object sender, RoutedEventArgs e)
        {
            if (_currentVideo == null) return;

            var next = Course.GetNextVideo(_currentVideo);
            if (next != null)
                LoadVideoIntoPlayer(next);
        }

        private void Player_VolumeChanged(
            object sender,
            VolumeChangedEventArgs e)
        {
            // Можно сохранить предпочтение громкости в настройках
        }

        private void Player_WindowStateChanged(
            object sender,
            VideoWindowStateChangedEventArgs e)
        {
            // При Fullscreen — скрываем заголовок и боковую панель
            // SmotrelPlayer сам управляет Reparenting через EnterFullscreen/ExitToNormal
            // Здесь только синхронизируем видимость NoFullScreen/FullScreen
            switch (e.NewState)
            {
                case VideoWindowState.Maximized:
                    // Плеер уже перемещён в _fullscreenWindow внутри SmotrelPlayer
                    // NoFullScreen скрываем чтобы не занимал место
                    NoFullScreen.Visibility = Visibility.Collapsed;
                    break;

                case VideoWindowState.PiP:
                    // При PiP основное окно остаётся с боковой панелью,
                    // но без плеера — можно показать заглушку
                    NoFullScreen.Visibility = Visibility.Visible;
                    break;

                case VideoWindowState.Normal:
                    NoFullScreen.Visibility = Visibility.Visible;
                    break;
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  КНОПКИ ЗАГОЛОВКА ОКНА
        // ════════════════════════════════════════════════════════════════════════

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            new MainWindow().Show();
            Close();
        }

        private void PipButton_Click(object sender, RoutedEventArgs e)
        {
            // Переключаем режим через DP плеера — он сам создаст PiP-окно
            Player.VideoWindowState = VideoWindowState.PiP;
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
            Application.Current.Shutdown();
        }

        private void Header_Down(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        // ── Подстройка окна при Maximized / Normal ────────────────────────────

        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            var tmplExit = ExitBtn.Template;
            var tmplBack = BackBtn.Template;

            var borderExit = tmplExit.FindName("RootBorder", ExitBtn) as Border;
            var borderBack = tmplBack.FindName("RootBorder", BackBtn) as Border;

            if (WindowState == WindowState.Maximized)
            {
                ColumnOfWindowManagmentBtns.Width = new GridLength(120);
                exitBtnDamper.Width = new GridLength(5);
                backBtnDamper.Width = new GridLength(5);
                DamperGap.Height = new GridLength(5);

                if (borderExit != null)
                {
                    borderExit.CornerRadius = new CornerRadius(0);
                    borderExit.Width = 40;
                }
                if (borderBack != null)
                    borderBack.CornerRadius = new CornerRadius(0);
            }
            else
            {
                exitBtnDamper.Width = new GridLength(0);
                backBtnDamper.Width = new GridLength(0);
                DamperGap.Height = new GridLength(0);

                if (borderExit != null)
                    borderExit.CornerRadius = new CornerRadius(0, 10, 0, 0);
                if (borderBack != null)
                    borderBack.CornerRadius = new CornerRadius(10, 0, 0, 0);
            }
        }
    }
}
