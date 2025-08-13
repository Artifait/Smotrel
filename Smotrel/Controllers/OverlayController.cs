
using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Controls;

namespace Smotrel.Controllers
{
    public class OverlayController : IDisposable
    {
        private readonly Grid _overlay;
        private readonly DispatcherTimer _overlayTimer;
        private readonly TimeSpan _timeout;
        private readonly TextBlock _centerIcon;
        private readonly DispatcherTimer _centerIconTimer;

        public OverlayController(Grid overlay, TextBlock centerIcon, TimeSpan timeout)
        {
            _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
            _centerIcon = centerIcon ?? throw new ArgumentNullException(nameof(centerIcon));
            _timeout = timeout;

            _overlayTimer = new DispatcherTimer { Interval = _timeout };
            _overlayTimer.Tick += (s, e) => HideOverlay();

            _centerIconTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _centerIconTimer.Tick += (s, e) =>
            {
                _centerIconTimer.Stop();
                var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(200));
                fadeOut.Completed += (_, __) => _centerIcon.Visibility = Visibility.Collapsed;
                _centerIcon.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            };
        }

        public void ShowOverlay(bool keepWhenPaused = true, bool isPlaying = true)
        {
            AnimateOverlayOpacity(1.0);
            _overlayTimer.Stop();
            if (!(keepWhenPaused && !isPlaying))
                _overlayTimer.Start();
        }

        public void HideOverlay()
        {
            AnimateOverlayOpacity(0.0);
            _overlayTimer.Stop();
        }

        private void AnimateOverlayOpacity(double toOpacity)
        {
            var animation = new DoubleAnimation { To = toOpacity, Duration = TimeSpan.FromMilliseconds(300) };
            _overlay.BeginAnimation(UIElement.OpacityProperty, animation);
        }

        public void ShowCenterFeedback(bool isPlaying)
        {
            bool wasPlayingBefore = !isPlaying;
            if (wasPlayingBefore)
            {
                _overlay.BeginAnimation(UIElement.OpacityProperty, null);
                _overlay.Opacity = 1.0;
                _overlay.Visibility = Visibility.Visible;
                _overlayTimer.Stop();
            }

            _centerIcon.Text = isPlaying ? "▶" : "⏸";
            _centerIcon.Visibility = Visibility.Visible;

            if (!(_centerIcon.RenderTransform is System.Windows.Media.ScaleTransform scale))
            {
                scale = new System.Windows.Media.ScaleTransform(1, 1);
                _centerIcon.RenderTransform = scale;
                _centerIcon.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            }

            _centerIcon.BeginAnimation(UIElement.OpacityProperty, null);
            scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);

            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(120));
            _centerIcon.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            var scaleAnim = new DoubleAnimation(0.8, 1.15, TimeSpan.FromMilliseconds(220))
            {
                AutoReverse = true,
                EasingFunction = new System.Windows.Media.Animation.BackEase { Amplitude = 0.3, EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleAnim);
            scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleAnim);

            _centerIconTimer.Stop();
            _centerIconTimer.Start();
        }

        public void Dispose()
        {
            _overlayTimer.Stop();
            _centerIconTimer.Stop();
        }
    }
}
