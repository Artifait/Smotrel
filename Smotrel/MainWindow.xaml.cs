using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

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

            for(int i = 1; i <= 1; i++)
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
    }
}
