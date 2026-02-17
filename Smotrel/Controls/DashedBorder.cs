using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Smotrel.Controls
{
    public class DashedBorder : Border
    {
        private static DoubleCollection? emptyDoubleCollection;

        private static DoubleCollection EmptyDoubleCollection()
        {
            if (emptyDoubleCollection == null)
            {
                var dc = new DoubleCollection();
                dc.Freeze();
                emptyDoubleCollection = dc;
            }
            return emptyDoubleCollection;
        }

        public static readonly DependencyProperty UseDashedBorderProperty =
            DependencyProperty.Register(nameof(UseDashedBorder),
                typeof(bool),
                typeof(DashedBorder),
                new FrameworkPropertyMetadata(false, OnUseDashedBorderChanged));

        public static readonly DependencyProperty DashedBorderBrushProperty =
            DependencyProperty.Register(nameof(DashedBorderBrush),
                typeof(Brush),
                typeof(DashedBorder),
                new FrameworkPropertyMetadata(null));

        public static readonly DependencyProperty StrokeDashArrayProperty =
            DependencyProperty.Register(nameof(StrokeDashArray),
                typeof(DoubleCollection),
                typeof(DashedBorder),
                new FrameworkPropertyMetadata(EmptyDoubleCollection()));

        public static readonly DependencyProperty AnimationDurationProperty =
            DependencyProperty.Register(nameof(AnimationDuration),
                typeof(Duration),
                typeof(DashedBorder),
                new FrameworkPropertyMetadata(new Duration(TimeSpan.FromSeconds(2))));

        private static void OnUseDashedBorderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((DashedBorder)d).UseDashedBorderChanged();
        }

        private Rectangle GetBoundRectangle()
        {
            var rectangle = new Rectangle();

            rectangle.SetBinding(Rectangle.StrokeThicknessProperty,
                new Binding { Source = this, Path = new PropertyPath("BorderThickness.Left") });

            rectangle.SetBinding(Rectangle.RadiusXProperty,
                new Binding { Source = this, Path = new PropertyPath("CornerRadius.TopLeft") });

            rectangle.SetBinding(Rectangle.RadiusYProperty,
                new Binding { Source = this, Path = new PropertyPath("CornerRadius.TopLeft") });

            rectangle.SetBinding(Rectangle.WidthProperty,
                new Binding { Source = this, Path = new PropertyPath(ActualWidthProperty) });

            rectangle.SetBinding(Rectangle.HeightProperty,
                new Binding { Source = this, Path = new PropertyPath(ActualHeightProperty) });

            return rectangle;
        }

        private Rectangle GetBackgroundRectangle()
        {
            var rectangle = GetBoundRectangle();
            rectangle.SetBinding(Rectangle.StrokeProperty,
                new Binding { Source = this, Path = new PropertyPath(BackgroundProperty) });
            return rectangle;
        }

        private Rectangle GetDashedRectangle()
        {
            var rectangle = GetBoundRectangle();

            rectangle.SetBinding(Rectangle.StrokeDashArrayProperty,
                new Binding { Source = this, Path = new PropertyPath(StrokeDashArrayProperty) });

            rectangle.SetBinding(Rectangle.StrokeProperty,
                new Binding { Source = this, Path = new PropertyPath(DashedBorderBrushProperty) });

            var animation = new DoubleAnimation
            {
                From = 360,
                To = 0,
                Duration = AnimationDuration,
                RepeatBehavior = RepeatBehavior.Forever
            };

            rectangle.BeginAnimation(Rectangle.StrokeDashOffsetProperty, animation);

            Panel.SetZIndex(rectangle, 2);

            return rectangle;
        }

        private VisualBrush CreateDashedBorderBrush()
        {
            var brush = new VisualBrush();
            var grid = new Grid();

            grid.Children.Add(GetBackgroundRectangle());
            grid.Children.Add(GetDashedRectangle());

            brush.Visual = grid;

            return brush;
        }

        private void UseDashedBorderChanged()
        {
            if (UseDashedBorder)
                BorderBrush = CreateDashedBorderBrush();
            else
                ClearValue(BorderBrushProperty);
        }

        public bool UseDashedBorder
        {
            get => (bool)GetValue(UseDashedBorderProperty);
            set => SetValue(UseDashedBorderProperty, value);
        }

        public Brush DashedBorderBrush
        {
            get => (Brush)GetValue(DashedBorderBrushProperty);
            set => SetValue(DashedBorderBrushProperty, value);
        }

        public DoubleCollection StrokeDashArray
        {
            get => (DoubleCollection)GetValue(StrokeDashArrayProperty);
            set => SetValue(StrokeDashArrayProperty, value);
        }

        public Duration AnimationDuration
        {
            get => (Duration)GetValue(AnimationDurationProperty);
            set => SetValue(AnimationDurationProperty, value);
        }
    }
}
