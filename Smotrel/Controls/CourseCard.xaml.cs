using System.Windows;
using System.Windows.Controls;


namespace Smotrel.Controls
{
    public partial class CourseCard : UserControl
    {
        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register("Label", 
                typeof(string), typeof(CourseCard),
                new UIPropertyMetadata(String.Empty, new PropertyChangedCallback(LabelChanged)));

        private static void LabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            CourseCard s = (CourseCard)d;
            TextBlock tb = s.labelDisplay;
            tb.Text = e.NewValue as string;
        }

        public CourseCard() => InitializeComponent();
    }
}
