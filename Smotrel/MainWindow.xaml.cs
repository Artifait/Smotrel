using Smotrel.Controls;
using Smotrel.DialogWIndows;
using System;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace Smotrel
{
    public class VData
    {
        public VData() { }
        public VData(string name) => Name = name;

        public string Name { get; set; }
        public string FullPath { get; set; } = "EMPTY";

        public override string ToString()
        {
            return Name;
        }
    }

    public partial class MainWindow : Window
    {
        public ObservableCollection<VData> Materials { get; set; }
        public MainWindow()
        {
            InitializeComponent();
            Materials = new ObservableCollection<VData>();

            for(int i = 1; i <= 0; i++)
            {
                Materials.Add(new("Akura " + i) { FullPath = "Sati " + Math.Pow(i, 2) });
            }

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
            //AddCourseWindow addCourseWindow = new AddCourseWindow();

            //var result = addCourseWindow.ShowDialog();
            //if (result == true)
            //{

            //}

            Materials.Add(new() { Name = GenerateRandomString() });
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
    }
}
