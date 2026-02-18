using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.Win32;
using Smotrel.Models;
using Smotrel.Services;

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
        public List<ChapterSummary> ChapterSummaries { get; set; } = new List<ChapterSummary>();
    }

    public partial class AddCourseWindow : Window, INotifyPropertyChanged
    {
        public CourseModel? OutCourseModel { get; set; } = null;

        private string _selectedFolderPath = string.Empty;
        public string SelectedFolderPath
        {
            get => _selectedFolderPath;
            set
            {
                if (_selectedFolderPath == value) return;
                _selectedFolderPath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsAddEnabled));
            }
        }

        public bool IsAddEnabled => !string.IsNullOrWhiteSpace(SelectedFolderPath) && Directory.Exists(SelectedFolderPath);

        private bool _isStatsVisible;
        public bool IsStatsVisible
        {
            get => _isStatsVisible;
            set
            {
                if (_isStatsVisible == value) return;
                _isStatsVisible = value;
                OnPropertyChanged();
            }
        }

        private string _courseTitle = string.Empty;
        public string CourseTitle
        {
            get => _courseTitle;
            set
            {
                if (_courseTitle == value) return;
                _courseTitle = value;
                OnPropertyChanged();
            }
        }

        private CourseStatsModel _courseStats = new CourseStatsModel();
        public CourseStatsModel CourseStats
        {
            get => _courseStats;
            set
            {
                _courseStats = value;
                OnPropertyChanged();
            }
        }

        public AddCourseWindow()
        {
            InitializeComponent();
            DataContext = this;
            IsStatsVisible = false;
            CourseStats = new CourseStatsModel();
            CourseTitle = string.Empty;
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog()
            {
                Multiselect = false,
                Title = "Выберите папку с курсом"
            };

            var res = dlg.ShowDialog();
            if (res == true)
            {
                SelectedFolderPath = dlg.FolderName;
                try
                {
                    var builder = new CourseBuilder();
                    var model = builder.BuildFromDirectory(SelectedFolderPath);
                    if (model != null && model.MainChapter != null)
                    {
                        OutCourseModel = model;
                        CourseTitle = string.IsNullOrWhiteSpace(model.Label) ? Path.GetFileName(SelectedFolderPath) ?? model.Label : model.Label;
                        CourseStats = BuildStats(model);
                        OnPropertyChanged(nameof(CourseStats));
                        IsStatsVisible = true;
                    }
                    else
                    {
                        OutCourseModel = null;
                        CourseTitle = string.Empty;
                        CourseStats = new CourseStatsModel();
                        IsStatsVisible = false;
                    }
                }
                catch (Exception ex)
                {
                    OutCourseModel = null;
                    CourseTitle = string.Empty;
                    CourseStats = new CourseStatsModel();
                    IsStatsVisible = false;
                    MessageBox.Show(this, "Не удалось собрать информацию о курсе: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                OnPropertyChanged(nameof(IsAddEnabled));
            }

            if(IsAddEnabled)
            {
                AddButton.Content = "Добавить курс";
            }
            else
            {
                AddButton.Content = "Путь не задан";
            }
        }

        private CourseStatsModel BuildStats(CourseModel model)
        {
            var stats = new CourseStatsModel();
            if (model?.MainChapter == null) return stats;

            int totalVideos = 0;
            int totalChaps = 0;
            int maxDepth = 0;
            var chapterSummaries = new List<ChapterSummary>();

            void Traverse(ChapterCourseModel ch, int depth)
            {
                totalChaps++;
                maxDepth = Math.Max(maxDepth, depth);
                int vidsHere = ch.Videos?.Count ?? 0;
                totalVideos += vidsHere;
                if (depth == 1)
                {
                    chapterSummaries.Add(new ChapterSummary
                    {
                        Title = ch.Title,
                        Info = $"Видео: {vidsHere}, Подглав: {(ch.Chapters?.Count ?? 0)}"
                    });
                }
                if (ch.Chapters != null)
                {
                    foreach (var c in ch.Chapters)
                    {
                        Traverse(c, depth + 1);
                    }
                }
            }

            Traverse(model.MainChapter, 1);

            stats.TotalVideos = totalVideos;
            stats.TotalChapters = totalChaps;
            stats.MaxDepth = maxDepth;
            stats.ChapterSummaries = chapterSummaries;

            return stats;
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsAddEnabled)
            {
                MessageBox.Show(this, "Укажите путь до желаемого курса.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (OutCourseModel != null)
            {
                OutCourseModel.Label = string.IsNullOrWhiteSpace(CourseTitle) ? OutCourseModel.Label : CourseTitle;
            }

            DialogResult = true;
            Close();
        }

        private void Header_Down(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try { DragMove(); } catch { }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
