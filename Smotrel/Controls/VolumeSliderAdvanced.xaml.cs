using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Smotrel.Controls
{
    public class VolumeIconThreshold
    {
        public double Threshold { get; set; }
        public string? IconText { get; set; }
        public Geometry? IconGeometry { get; set; }

        public VolumeIconThreshold() { }

        public VolumeIconThreshold(double threshold, string iconText)
        {
            Threshold = threshold;
            IconText = iconText;
        }

        public VolumeIconThreshold(double threshold, Geometry geometry)
        {
            Threshold = threshold;
            IconGeometry = geometry;
        }
    }

    public partial class VolumeSliderAdvanced : UserControl
    {
        public event EventHandler<double>? VolumeChanged;

        #region Dependency Properties

        public static readonly DependencyProperty VolumeProperty =
            DependencyProperty.Register(nameof(Volume), typeof(double), typeof(VolumeSliderAdvanced),
                new PropertyMetadata(0.5, OnVolumeChanged));

        public double Volume
        {
            get => (double)GetValue(VolumeProperty);
            set => SetValue(VolumeProperty, Math.Max(0, Math.Min(1, value)));
        }

        public static readonly DependencyProperty IconThresholdsProperty =
            DependencyProperty.Register(nameof(IconThresholds), typeof(ObservableCollection<VolumeIconThreshold>),
                typeof(VolumeSliderAdvanced), new PropertyMetadata(null, OnIconThresholdsChanged));

        public ObservableCollection<VolumeIconThreshold> IconThresholds
        {
            get
            {
                var collection = (ObservableCollection<VolumeIconThreshold>?)GetValue(IconThresholdsProperty);
                if (collection == null)
                {
                    collection = new ObservableCollection<VolumeIconThreshold>
                    {
                        new VolumeIconThreshold(0.0, "🔇"),
                        new VolumeIconThreshold(0.3, "🔈"),
                        new VolumeIconThreshold(0.7, "🔉"),
                        new VolumeIconThreshold(1.0, "🔊")
                    };
                    SetValue(IconThresholdsProperty, collection);
                }
                return collection;
            }
            set => SetValue(IconThresholdsProperty, value);
        }

        private static readonly DependencyPropertyKey CurrentIconGeometryPropertyKey =
            DependencyProperty.RegisterReadOnly(nameof(CurrentIconGeometry), typeof(Geometry),
                typeof(VolumeSliderAdvanced), new PropertyMetadata(null));

        public static readonly DependencyProperty CurrentIconGeometryProperty =
            CurrentIconGeometryPropertyKey.DependencyProperty;

        public Geometry? CurrentIconGeometry
        {
            get => (Geometry?)GetValue(CurrentIconGeometryProperty);
            protected set => SetValue(CurrentIconGeometryPropertyKey, value);
        }

        private static readonly DependencyPropertyKey CurrentIconTextPropertyKey =
            DependencyProperty.RegisterReadOnly(nameof(CurrentIconText), typeof(string),
                typeof(VolumeSliderAdvanced), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty CurrentIconTextProperty =
            CurrentIconTextPropertyKey.DependencyProperty;

        public string CurrentIconText
        {
            get => (string)GetValue(CurrentIconTextProperty);
            protected set => SetValue(CurrentIconTextPropertyKey, value);
        }

        #endregion

        private bool _dragging;

        public VolumeSliderAdvanced()
        {
            InitializeComponent();

            Loaded += (_, _) =>
            {
                UpdateVisuals();
                UpdateIcon();
            };

            SizeChanged += (_, _) => UpdateVisuals();

            if (IconThresholds != null)
                AttachCollectionChanged(IconThresholds);
        }

        private static void OnVolumeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VolumeSliderAdvanced ctrl)
            {
                ctrl.VolumeChanged?.Invoke(ctrl, (double)e.NewValue);
                if (!ctrl._dragging)
                    ctrl.UpdateVisuals();

                ctrl.UpdateIcon();
            }
        }

        private static void OnIconThresholdsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VolumeSliderAdvanced ctrl)
            {
                if (e.OldValue is INotifyCollectionChanged oldCol)
                    oldCol.CollectionChanged -= ctrl.IconThresholds_CollectionChanged;

                if (e.NewValue is ObservableCollection<VolumeIconThreshold> newCol)
                    ctrl.AttachCollectionChanged(newCol);

                ctrl.UpdateIcon();
            }
        }

        private void AttachCollectionChanged(ObservableCollection<VolumeIconThreshold> col)
        {
            col.CollectionChanged += IconThresholds_CollectionChanged;
        }

        private void IconThresholds_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateIcon();
        }

        private static double Clamp(double v) => Math.Max(0, Math.Min(1, v));

        private void UpdateVisuals()
        {
            if (PART_Fill == null || PART_Root == null || PART_Thumb == null) return;

            double width = PART_Root.ActualWidth * Clamp(Volume);
            PART_Fill.Width = width;

            double left = width - PART_Thumb.Width / 2.0;
            left = Math.Max(-PART_Thumb.Width / 2.0, Math.Min(left, PART_Root.ActualWidth - PART_Thumb.Width / 2.0));
            Canvas.SetLeft(PART_Thumb, left);
            Canvas.SetTop(PART_Thumb, (PART_Root.ActualHeight - PART_Thumb.Height) / 2.0);
        }

        private void UpdateIcon()
        {
            if (IconThresholds == null || IconThresholds.Count == 0)
            {
                CurrentIconGeometry = null;
                CurrentIconText = "🔊";
                ApplyIconVisibility();
                return;
            }

            var chosen = IconThresholds
                .OrderBy(it => it.Threshold)
                .LastOrDefault(it => Clamp(it.Threshold) <= Clamp(Volume))
                ?? IconThresholds.First();

            CurrentIconGeometry = chosen.IconGeometry;
            CurrentIconText = chosen.IconText ?? string.Empty;

            ApplyIconVisibility();
            AnimateIconChange();
        }

        private void ApplyIconVisibility()
        {
            if (PART_IconPath == null || PART_IconText == null) return;

            if (CurrentIconGeometry != null)
            {
                PART_IconPath.Visibility = Visibility.Visible;
                PART_IconText.Visibility = Visibility.Collapsed;
            }
            else
            {
                PART_IconPath.Visibility = Visibility.Collapsed;
                PART_IconText.Visibility = Visibility.Visible;
            }
        }

        private void AnimateIconChange()
        {
            if (PART_IconScale == null) return;

            var anim = new DoubleAnimation
            {
                From = 1.0,
                To = 1.18,
                Duration = TimeSpan.FromMilliseconds(120),
                AutoReverse = true,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            PART_IconScale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            PART_IconScale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        #region Mouse handlers

        private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragging = true;
            Mouse.Capture(PART_Root);
            var pos = e.GetPosition(PART_Root);
            SetVolumeFromPosition(pos.X);
            e.Handled = true;
        }

        private void Root_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var pos = e.GetPosition(PART_Root);
            SetVolumeFromPosition(pos.X);
        }

        private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            Mouse.Capture(null);
        }

        private void SetVolumeFromPosition(double x)
        {
            if (PART_Root == null) return;
            var v = x / PART_Root.ActualWidth;
            Volume = Clamp(v);
            UpdateVisuals();
        }

        #endregion
    }
}
