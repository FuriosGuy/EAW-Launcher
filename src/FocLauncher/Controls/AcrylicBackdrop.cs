using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FocLauncher.Controls
{
    public sealed class AcrylicBackdrop : ContentControl
    {
        private Image _backdropImage;
        private FrameworkElement _artworkSurface;
        private TranslateTransform _backdropTransform;
        private bool _layoutUpdateQueued;

        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register(
                nameof(CornerRadius),
                typeof(CornerRadius),
                typeof(AcrylicBackdrop),
                new FrameworkPropertyMetadata(new CornerRadius(8)));

        public static readonly DependencyProperty UseArtworkBackdropProperty =
            DependencyProperty.Register(
                nameof(UseArtworkBackdrop),
                typeof(bool),
                typeof(AcrylicBackdrop),
                new FrameworkPropertyMetadata(true));

        public CornerRadius CornerRadius
        {
            get => (CornerRadius)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        public bool UseArtworkBackdrop
        {
            get => (bool)GetValue(UseArtworkBackdropProperty);
            set => SetValue(UseArtworkBackdropProperty, value);
        }

        public override void OnApplyTemplate()
        {
            if (_backdropImage != null)
            {
                _backdropImage.LayoutUpdated -= BackdropImage_OnLayoutUpdated;
            }

            base.OnApplyTemplate();

            _backdropImage = GetTemplateChild("BackdropImage") as Image;
            if (_backdropImage != null)
            {
                _backdropImage.RenderTransform = _backdropTransform = new TranslateTransform();
                _backdropImage.LayoutUpdated += BackdropImage_OnLayoutUpdated;
            }

            QueueBackdropLayout();
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            Loaded += AcrylicBackdrop_OnLoaded;
            Unloaded += AcrylicBackdrop_OnUnloaded;
            SizeChanged += AcrylicBackdrop_OnSizeChanged;
        }

        private void AcrylicBackdrop_OnLoaded(object sender, RoutedEventArgs e)
        {
            QueueBackdropLayout();
        }

        private void AcrylicBackdrop_OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_artworkSurface != null)
            {
                _artworkSurface.LayoutUpdated -= ArtworkSurface_OnLayoutUpdated;
            }

            _artworkSurface = null;
        }

        private void AcrylicBackdrop_OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            QueueBackdropLayout();
        }

        private void BackdropImage_OnLayoutUpdated(object sender, EventArgs e)
        {
            QueueBackdropLayout();
        }

        private void ArtworkSurface_OnLayoutUpdated(object sender, EventArgs e)
        {
            QueueBackdropLayout();
        }

        private void QueueBackdropLayout()
        {
            if (_layoutUpdateQueued || !IsLoaded)
            {
                return;
            }

            _layoutUpdateQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _layoutUpdateQueued = false;
                UpdateBackdropLayout();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void UpdateBackdropLayout()
        {
            if (_backdropImage == null || ActualWidth <= 0 || ActualHeight <= 0)
            {
                return;
            }

            var window = Window.GetWindow(this);
            var artworkSurface = window?.FindName("ArtworkSurfaceImage") as FrameworkElement;
            if (artworkSurface == null || artworkSurface.ActualWidth <= 0 || artworkSurface.ActualHeight <= 0)
            {
                return;
            }

            if (!ReferenceEquals(_artworkSurface, artworkSurface))
            {
                if (_artworkSurface != null)
                {
                    _artworkSurface.LayoutUpdated -= ArtworkSurface_OnLayoutUpdated;
                }

                _artworkSurface = artworkSurface;
                _artworkSurface.LayoutUpdated += ArtworkSurface_OnLayoutUpdated;
            }

            var artworkOrigin = artworkSurface.TranslatePoint(new Point(0, 0), this);
            var width = artworkSurface.ActualWidth;
            var height = artworkSurface.ActualHeight;
            if (!AreClose(_backdropImage.Width, width))
            {
                _backdropImage.Width = width;
            }

            if (!AreClose(_backdropImage.Height, height))
            {
                _backdropImage.Height = height;
            }

            if (_backdropTransform != null)
            {
                _backdropTransform.X = -artworkOrigin.X;
                _backdropTransform.Y = -artworkOrigin.Y;
            }
        }

        private static bool AreClose(double left, double right)
        {
            return Math.Abs(left - right) < 0.5;
        }
    }
}
