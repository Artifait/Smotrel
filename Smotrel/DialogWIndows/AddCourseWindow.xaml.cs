using Microsoft.Win32;
using Smotrel.Models;
using Smotrel.Services;
using Smotrel.Views;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;

namespace Smotrel.DialogWindows
{
    public class ChapterSummary
    {
        public string Title { get; set; } = string.Empty;
        public string Info { get; set; } = string.Empty;
    }

    public class CourseStatsModel
    {
        public int TotalVideos { get; set; }
        public int TotalChapters { get; set; }
        public int MaxDepth { get; set; }
        public List<ChapterSummary> ChapterSummaries { get; set; } = [];
    }

    public partial class AddCourseWindow : Window, INotifyPropertyChanged
    {
        // ── Выходная модель ───────────────────────────────────────────────────

        public CourseModel? OutCourseModel { get; set; }

        // ── Отмена сканирования ───────────────────────────────────────────────

        private CancellationTokenSource? _cts;

        // ── Bindable свойства ─────────────────────────────────────────────────

        private string _selectedFolderPath = string.Empty;
        public string SelectedFolderPath
        {
            get => _selectedFolderPath;
            set { _selectedFolderPath = value; Notify(); Notify(nameof(IsAddEnabled)); }
        }

        public bool IsAddEnabled =>
            !string.IsNullOrWhiteSpace(SelectedFolderPath)
            && Directory.Exists(SelectedFolderPath)
            && !_isScanning
            && OutCourseModel != null;

        private bool _isScanning;
        public bool IsScanning
        {
            get => _isScanning;
            set { _isScanning = value; Notify(); Notify(nameof(IsAddEnabled)); Notify(nameof(IsNotScanning)); }
        }

        public bool IsNotScanning => !_isScanning;

        private bool _isStatsVisible;
        public bool IsStatsVisible
        {
            get => _isStatsVisible;
            set { _isStatsVisible = value; Notify(); }
        }

        private string _courseTitle = string.Empty;
        public string CourseTitle
        {
            get => _courseTitle;
            set { _courseTitle = value; Notify(); }
        }

        private CourseStatsModel _courseStats = new();
        public CourseStatsModel CourseStats
        {
            get => _courseStats;
            set { _courseStats = value; Notify(); }
        }

        // ── Прогресс сканирования ─────────────────────────────────────────────

        private int _scanProgress;
        public int ScanProgress
        {
            get => _scanProgress;
            set { _scanProgress = value; Notify(); }
        }

        private string _scanCurrentFile = string.Empty;
        public string ScanCurrentFile
        {
            get => _scanCurrentFile;
            set { _scanCurrentFile = value; Notify(); }
        }

        private string _scanProgressText = string.Empty;
        public string ScanProgressText
        {
            get => _scanProgressText;
            set { _scanProgressText = value; Notify(); }
        }

        // ── Конструктор ───────────────────────────────────────────────────────

        public AddCourseWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        // ── Выбор папки и сканирование ────────────────────────────────────────

        private async void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            // Если сейчас идёт сканирование — отменяем
            if (_isScanning)
            {
                _cts?.Cancel();
                return;
            }

            var dlg = new OpenFolderDialog
            {
                Multiselect = false,
                Title = "Выберите папку с курсом"
            };

            if (dlg.ShowDialog() != true) return;

            // Проверяем дубликат
            if (MainWindow.Context.CourseCardPathExistsAsync(dlg.FolderName).GetAwaiter().GetResult())
            {
                MessageBox.Show(this, "Этот курс уже добавлен.", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedFolderPath = dlg.FolderName;
            OutCourseModel = null;
            IsStatsVisible = false;
            CourseTitle = string.Empty;
            CourseStats = new();

            await ScanCourseAsync(dlg.FolderName);

            Notify(nameof(IsAddEnabled));
        }

        private async Task ScanCourseAsync(string path)
        {
            IsScanning = true;
            ScanProgress = 0;
            ScanCurrentFile = "Сканирование структуры...";
            ScanProgressText = "0%";

            _cts = new CancellationTokenSource();

            var progress = new Progress<CourseBuilderProgress>(p =>
            {
                ScanProgress = p.Percent;
                ScanProgressText = $"{p.Current} / {p.Total}  ({p.Percent}%)";
                ScanCurrentFile = string.IsNullOrEmpty(p.FileName)
                    ? "Завершение..."
                    : p.FileName;
            });

            try
            {
                var builder = new CourseBuilderService();
                var model = await builder.BuildAsync(path, progress, _cts.Token);

                OutCourseModel = model;
                CourseTitle = string.IsNullOrWhiteSpace(model.Label)
                    ? Path.GetFileName(path)
                    : model.Label;
                CourseStats = BuildStats(model);
                IsStatsVisible = true;
            }
            catch (OperationCanceledException)
            {
                OutCourseModel = null;
                IsStatsVisible = false;
                ScanCurrentFile = "Отменено";
            }
            catch (Exception ex)
            {
                OutCourseModel = null;
                IsStatsVisible = false;
                MessageBox.Show(this, "Ошибка при сканировании: " + ex.Message,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsScanning = false;
                _cts = null;
            }
        }

        // ── Построение статистики ─────────────────────────────────────────────

        private static CourseStatsModel BuildStats(CourseModel model)
        {
            var stats = new CourseStatsModel();
            if (model?.MainChapter == null) return stats;

            int totalVideos = 0, totalChaps = 0, maxDepth = 0;
            var summaries = new List<ChapterSummary>();

            void Traverse(ChapterCourseModel ch, int depth)
            {
                totalChaps++;
                maxDepth = Math.Max(maxDepth, depth);
                totalVideos += ch.Videos?.Count ?? 0;

                if (depth == 1)
                    summaries.Add(new ChapterSummary
                    {
                        Title = ch.Title,
                        Info = $"Видео: {ch.Videos?.Count ?? 0}, Подглав: {ch.Chapters?.Count ?? 0}"
                    });

                if (ch.Chapters != null)
                    foreach (var c in ch.Chapters)
                        Traverse(c, depth + 1);
            }

            Traverse(model.MainChapter, 1);

            stats.TotalVideos = totalVideos;
            stats.TotalChapters = totalChaps;
            stats.MaxDepth = maxDepth;
            stats.ChapterSummaries = summaries;
            return stats;
        }

        // ── Кнопка "Добавить" ─────────────────────────────────────────────────

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsAddEnabled || OutCourseModel == null)
            {
                MessageBox.Show(this, "Укажите путь до желаемого курса.", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            OutCourseModel.Label =
                string.IsNullOrWhiteSpace(CourseTitle) ? OutCourseModel.Label : CourseTitle;

            DialogResult = true;
            Close();
        }

        // ── Управление окном ──────────────────────────────────────────────────

        private void Header_Down(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try { DragMove(); } catch { }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            Close();
        }

        // ── INotifyPropertyChanged ────────────────────────────────────────────

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string? p = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}