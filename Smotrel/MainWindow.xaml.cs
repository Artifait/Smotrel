using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms; // FolderBrowserDialog
using System.Windows.Threading;
using System.Windows.Input;
using System.Globalization;
using System.Windows.Data;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows;

namespace Smotrel
{
    public class SliderValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double doubleValue && parameter is Slider slider)
            {
                double percentage = doubleValue / slider.Maximum;
                return slider.ActualWidth * percentage;
            }
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class AppSettings
    {
        public string LastFolder { get; set; }
    }

    public static class WorkJson
    {
        private static readonly string settingsFile = "settings.json";
        public static AppSettings Settings { get; private set; } = new AppSettings();

        public static void LoadSettings()
        {
            if (File.Exists(settingsFile))
            {
                var json = File.ReadAllText(settingsFile);
                Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }

        public static void SaveSettings()
        {
            var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(settingsFile, json);
        }
    }

    public partial class MainWindow : Window
    {
        private List<bool> watchedFlags = new();
        private DispatcherTimer timer;
        private bool isDragging = false;
        private List<string> videoFiles = new();
        private int currentIndex = 0;
        private bool isFullscreen = false;
        private WindowState prevWindowState;
        private WindowStyle prevWindowStyle;
        private bool isPaused = false;
        private const double PanelWidth = 200;
        private const double ShowThreshold = 20;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            InitializeTimer();
            LoadLastUsedFolder();
        }

        private void InitializeTimer()
        {
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += Timer_Tick;
            timer.Start();
        }

        private void LoadLastUsedFolder()
        {
            WorkJson.LoadSettings();

            if (!string.IsNullOrEmpty(WorkJson.Settings.LastFolder) &&
                Directory.Exists(WorkJson.Settings.LastFolder))
            {
                LoadVideosFromFolder(WorkJson.Settings.LastFolder);
            }
            else
            {
                ChooseFolderAndLoadVideos();
            }
        }

        private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            var pos = e.GetPosition(this);
            // Если курсор в пределах ShowThreshold от правого края — показать панель
            if (pos.X >= this.ActualWidth - ShowThreshold)
                listPanel.Width = PanelWidth;
            else if (!IsMouseOverElement(listPanel, e))
                listPanel.Width = 0;
        }

        private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            listPanel.Width = 0;
        }

        private bool IsMouseOverElement(FrameworkElement element, System.Windows.Input.MouseEventArgs e)
        {
            var pos = e.GetPosition(element);
            return pos.X >= 0 && pos.Y >= 0 && pos.X <= element.ActualWidth && pos.Y <= element.ActualHeight;
        }

        // При выборе в списке — переключаем видео
        private void videoListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (videoListBox.SelectedIndex >= 0 && videoListBox.SelectedIndex < videoFiles.Count)
            {
                currentIndex = videoListBox.SelectedIndex;
                PlayCurrentVideo();
            }
        }


        private void ChooseFolderAndLoadVideos()
        {
            using var dialog = new FolderBrowserDialog { Description = "Выберите папку с видео" };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                // Сохраняем в настройки и в файл
                WorkJson.Settings.LastFolder = dialog.SelectedPath;
                WorkJson.SaveSettings();

                LoadVideosFromFolder(dialog.SelectedPath);
            }
            else
            {
                Close();
            }
        }

        private void LoadVideosFromFolder(string folderPath)
        {
            videoFiles = Directory.GetFiles(folderPath, "*.*")
                .Where(f => f.EndsWith(".mp4") || f.EndsWith(".avi") || f.EndsWith(".mkv") || f.EndsWith(".wmv"))
                .OrderBy(f => f)
                .ToList();

            watchedFlags = new List<bool>(new bool[videoFiles.Count]);

            if (videoFiles.Count > 0)
            {
                currentIndex = 0;
                UpdateVideoListUI();
                PlayCurrentVideo();
            }
            else
            {
                System.Windows.MessageBox.Show("Нет видеофайлов в выбранной папке.");
            }
        }

        private void PlayCurrentVideo()
        {
            if (currentIndex >= 0 && currentIndex < videoFiles.Count)
            {
                mediaElement.Source = new Uri(videoFiles[currentIndex]);
                mediaElement.Play();

                watchedFlags[currentIndex] = true;
                UpdateVideoListUI();
                UpdateRemainingTimeDisplay();
            }
        }
        private TimeSpan CalculateRemainingTime()
        {
            TimeSpan remaining = TimeSpan.Zero;

            for (int i = 0; i < videoFiles.Count; i++)
            {
                if (!watchedFlags[i])
                {
                    var duration = GetVideoDuration(videoFiles[i]);
                    remaining += duration;
                }
            }

            return remaining;
        }
        private void BtnChooseFolder_Click(object sender, RoutedEventArgs e)
        {
            ChooseFolderAndLoadVideos();
        }
        private TimeSpan GetVideoDuration(string path)
        {
            try
            {
                var shell = Microsoft.WindowsAPICodePack.Shell.ShellObject.FromParsingName(path);
                var durationProp = shell.Properties.System.Media.Duration;
                ulong durationIn100Ns = durationProp.Value ?? 0;
                return TimeSpan.FromTicks((long)(durationIn100Ns / 100));
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }


        private void UpdateRemainingTimeDisplay()
        {
            var remaining = CalculateRemainingTime();
            remainingTimeText.Text = $"Осталось: {remaining.Hours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
        }

        private void UpdateVideoListUI()
        {
            videoListBox.ItemsSource = videoFiles.Select((f, i) =>
                $"{(watchedFlags[i] ? "✔️ " : "")}{Path.GetFileName(f)}").ToList();

            videoListBox.SelectedIndex = currentIndex;
            videoListBox.ScrollIntoView(videoListBox.SelectedItem);
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (!isDragging && mediaElement.NaturalDuration.HasTimeSpan)
            {
                videoSlider.Maximum = mediaElement.NaturalDuration.TimeSpan.TotalSeconds;
                videoSlider.Value = mediaElement.Position.TotalSeconds;
            }
        }

        private void videoSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (isDragging)
                mediaElement.Position = TimeSpan.FromSeconds(videoSlider.Value);
        }

        private void Slider_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            isDragging = true;
        }

        private void Slider_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            isDragging = false;
            mediaElement.Position = TimeSpan.FromSeconds(videoSlider.Value);
        }


        private void MediaElement_MediaEnded(object sender, RoutedEventArgs e)
        {
            if (currentIndex + 1 < videoFiles.Count)
            {
                currentIndex++;
                PlayCurrentVideo();
            }
            else
            {
                System.Windows.MessageBox.Show("Вы просмотрели все видео.");
            }
        }

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            if (currentIndex > 0)
            {
                currentIndex--;
                PlayCurrentVideo();
            }
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (currentIndex + 1 < videoFiles.Count)
            {
                currentIndex++;
                PlayCurrentVideo();
            }
        }

        private void BtnPausePlay_Click(object sender, RoutedEventArgs e)
        {
            if (isPaused)
            {
                mediaElement.Play();
            }
            else
            {
                mediaElement.Pause();
            }
            isPaused = !isPaused;
        }

        private void BtnSpeedUp_Click(object sender, RoutedEventArgs e)
        {
            if (mediaElement.SpeedRatio < 4.0)
                mediaElement.SpeedRatio += 0.5;
        }

        private void BtnNormalSpeed_Click(object sender, RoutedEventArgs e)
        {
            mediaElement.SpeedRatio = 1.0;
        }

        private void BtnFullscreen_Click(object sender, RoutedEventArgs e)
        {
            if (!isFullscreen)
            {
                prevWindowState = WindowState;
                prevWindowStyle = WindowStyle;
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
            }
            else
            {
                WindowStyle = prevWindowStyle;
                WindowState = prevWindowState;
            }
            isFullscreen = !isFullscreen;
        }
    }
}
