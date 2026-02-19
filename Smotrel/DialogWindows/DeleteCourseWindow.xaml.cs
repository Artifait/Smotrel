using Smotrel.Controls;
using System;
using System.Windows;
using System.Windows.Input;

namespace Smotrel.DialogWindows
{
    public partial class DeleteCourseWindow : Window
    {
        private readonly CourseCard deletingCard;

        public DeleteCourseWindow(CourseCard card)
        {
            InitializeComponent();
            deletingCard = card;

            if (deletingCard != null)
                CourseNameText.Text = deletingCard.Label;
        }

        private void Header_Down(object sender, MouseButtonEventArgs e)
        {
            try { DragMove(); } catch { }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (deletingCard == null)
            {
                DialogResult = false;
                Close();
                return;
            }

            DialogResult = true;
            Close();
        }
    }
}
