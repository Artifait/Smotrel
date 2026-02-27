using Microsoft.EntityFrameworkCore;
using Smotrel.Controls;
using Smotrel.DialogWindows;
using Smotrel.Models;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Smotrel.Views
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<CourseCardModel> Materials { get; set; }
        public static SmotrelContext Context { get; private set; } = new();
        public MainWindow()
        {
            InitializeComponent();

            Materials = new ObservableCollection<CourseCardModel>(Context.CourseCards);
            Data.ItemsSource = Materials;
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
        }

        private void Header_Down(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {

        }


        private void AddMaterial_Click(object sender, RoutedEventArgs e)
        {
            var addCourseWindow = new AddCourseWindow();

            var result = addCourseWindow.ShowDialog();
            if (result == true)
            {
                if (addCourseWindow.OutCourseModel != null)
                {
                    var course = addCourseWindow.OutCourseModel;

                    var model = new CourseCardModel
                    {
                        Label = addCourseWindow.OutCourseModel.Label ?? string.Empty,
                        Path = addCourseWindow.SelectedFolderPath ?? string.Empty,
                        Course = course
                    };
                    Context.Courses.Add(course);
                    Context.CourseCards.Add(model);
                    Context.SaveChanges();

                    Materials.Add(model);
                }
            }
        }

        CourseCard? pastClickedCard = null;
        private void CourseCard_Click(object sender, RoutedEventArgs e)
        {
            var clickedCard = e.OriginalSource as CourseCard;

            if(clickedCard != null)
                clickedCard.IsSelected = !clickedCard.IsSelected;

            if(pastClickedCard != null && clickedCard != null && pastClickedCard != clickedCard)
            {
                pastClickedCard.IsSelected = false;
            }

            pastClickedCard = clickedCard;
        }

        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            var templateOfExitBtn = ExitBtn.Template;
            var templateOfSettingsBtn = SettingsBtn.Template;

            var rootBorderOfExitBtn = templateOfExitBtn.FindName("RootBorder", ExitBtn) as Border;
            var rootBorderOfSettingsBtn = templateOfSettingsBtn.FindName("RootBorder", SettingsBtn) as Border;

            if (WindowState == WindowState.Maximized)
            {
                ColumnOfWindowManagmentBtns.Width = new GridLength(120);

                exitBtnDamper.Width = new GridLength(5);
                settingsBtnDamper.Width = new GridLength(5);
                DamperGap.Height = new GridLength(5);

                if (rootBorderOfExitBtn != null)
                {
                    rootBorderOfExitBtn.CornerRadius = new CornerRadius(0);
                    rootBorderOfExitBtn.Width = 40;
                }

                if (rootBorderOfSettingsBtn != null)
                    rootBorderOfSettingsBtn.CornerRadius = new CornerRadius(0);
            }
            else
            {
                exitBtnDamper.Width = new GridLength(0);
                settingsBtnDamper.Width = new GridLength(0);
                DamperGap.Height = new GridLength(0);

                if (rootBorderOfExitBtn != null)
                    rootBorderOfExitBtn.CornerRadius = new CornerRadius(0, 10, 0, 0);

                if (rootBorderOfSettingsBtn != null)
                    rootBorderOfSettingsBtn.CornerRadius = new CornerRadius(10, 0, 0, 0);
            }
        }

        private void CourseCardDeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            var card = e.OriginalSource as CourseCard;
            if (card == null) return;

            var model = card.DataContext as CourseCardModel;
            if (model == null) return;

            var deleteCourseWindow = new DeleteCourseWindow(card);
            var res = deleteCourseWindow.ShowDialog();

            if (res == true)
            {
                Materials.Remove(model);
                Context.CourseCards.Remove(model);
                Context.SaveChanges();
            }
        }

        private void CourseCardPlayBtn_Click(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not CourseCard card) return;
            if (card.DataContext is not CourseCardModel model) return;

            var courseId = model.CourseId;

            var course = Context.Courses
                .Include(c => c.MainChapter)
                .AsSplitQuery()
                .SingleOrDefault(c => c.Id == courseId);

            if (course?.MainChapter == null) return;

            void LoadChapterTree(ChapterCourseModel chapter)
            {
                Context.Entry(chapter)
                    .Collection(c => c.Videos)
                    .Query()
                    .OrderBy(v => v.RelativeIndex)
                    .Load();

                Context.Entry(chapter)
                    .Collection(c => c.Chapters)
                    .Query()
                    .OrderBy(c => c.RelativeIndex)
                    .Load();

                foreach (var child in chapter.Chapters)
                {
                    LoadChapterTree(child);
                }
            }

            LoadChapterTree(course.MainChapter);

            var mainPlayWindow = new MainPlayer(course);
            mainPlayWindow.Show();

            Close();
        }
    }
}
