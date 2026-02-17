using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Smotrel.Controls
{
    public partial class CourseCard : UserControl
    {
        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public bool IsSelected
        {
            get => (bool)GetValue(IsSelectedProperty);
            set => SetValue(IsSelectedProperty, value);
        }

        public event RoutedEventHandler CardClick
        {
            add { AddHandler(CardClickEvent, value); }
            remove { RemoveHandler(CardClickEvent, value); }
        }

        public static readonly DependencyProperty IsSelectedProperty =
            DependencyProperty.Register("IsSelected",
                typeof(bool), typeof(CourseCard),
                new UIPropertyMetadata(false, new PropertyChangedCallback(IsSelectedChanged)));

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register("Label",
                typeof(string), typeof(CourseCard),
                new UIPropertyMetadata(String.Empty, new PropertyChangedCallback(LabelChanged)));

        public static readonly RoutedEvent CardClickEvent = EventManager.RegisterRoutedEvent(
            "CardClick", RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(CourseCard));

        private static void IsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (CourseCard)d;
            if (control.ItemBorder == null)
                return;

            if ((bool)e.NewValue)
            {
                var drawingGroup = new DrawingGroup();

                var geometryGroup = new GeometryGroup();
                geometryGroup.Children.Add(new RectangleGeometry(new Rect(0, 0, 50, 50)));
                geometryGroup.Children.Add(new RectangleGeometry(new Rect(50, 50, 50, 50)));

                var geometryDrawing = new GeometryDrawing
                {
                    Brush = new SolidColorBrush(Colors.LightGoldenrodYellow),
                    Geometry = geometryGroup
                };

                drawingGroup.Children.Add(geometryDrawing);

                var brush = new DrawingBrush
                {
                    Viewport = new Rect(0, 0, 8, 8),
                    ViewportUnits = BrushMappingMode.Absolute,
                    TileMode = TileMode.Tile,
                    Drawing = drawingGroup
                };

                control.ItemBorder.BorderBrush = brush;
            }
            else
            {
                control.ItemBorder.BorderBrush = control.ItemBorder.Background;
            }
        }

        private static void LabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            CourseCard s = (CourseCard)d;
            TextBlock tb = s.labelDisplay;
            tb.Text = e.NewValue as string;
        }

        public CourseCard()
        {
            InitializeComponent();

            MouseDown += (s, e) => RaiseEvent(new RoutedEventArgs(CardClickEvent));
        }

        private void HighlightCard(object sender, System.Windows.Input.MouseEventArgs e)
        {
            var card = (CourseCard)sender;
            if (card != null)
            {
                if (!IsSelected)
                {
                    card.ItemBorder.BorderBrush = new SolidColorBrush(Colors.WhiteSmoke);
                }
            }
        }

        private void UnHighlightCard(object sender, System.Windows.Input.MouseEventArgs e)
        {
            var card = (CourseCard)sender;
            if (card != null)
            {
                if (!IsSelected)
                {
                    card.ItemBorder.BorderBrush = card.ItemBorder.Background;
                }
            }
        }
    }
}
