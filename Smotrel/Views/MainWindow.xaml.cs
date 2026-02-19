using Smotrel.Controls;
using Smotrel.DialogWindows;
using Smotrel.Models;
using System;
using System.Collections.ObjectModel;
using System.Text;
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

        private void MenuButton_Click(object sender, RoutedEventArgs e)
        {

        }

        static int data = 0;
        private static Random _random = new Random();
        public static string GenerateRandomString(int maxLength = 30)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            // Выбираем случайную длину от 1 до 30
            int length = _random.Next(1, maxLength + 1);

            StringBuilder result = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                result.Append(chars[_random.Next(chars.Length)]);
            }
            return result.ToString();
        }

        private void AddMaterial_Click(object sender, RoutedEventArgs e)
        {
            var addCourseWindow = new AddCourseWindow();

            var result = addCourseWindow.ShowDialog();
            if (result == true)
            {
                if (addCourseWindow.OutCourseModel != null)
                {
                    var model = new CourseCardModel
                    {
                        Label = addCourseWindow.OutCourseModel.Label ?? string.Empty,
                        Path = addCourseWindow.SelectedFolderPath ?? string.Empty
                    };

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
            
            //MessageBox.Show($"Clicked on: {clickedCard?.Label}");
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
            var card = e.OriginalSource as CourseCard;
            MessageBox.Show($"Play button clicked on: {card?.Label}");
        }
    }
}
