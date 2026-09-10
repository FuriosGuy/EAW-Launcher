using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Navigation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using FocLauncher.Controls;
using FocLauncher.Dialogs;
using FocLauncher.Mods;
using FocLauncher.Profiles;
using FocLauncher.Theming;
using FocLauncher.Utilities;
using Microsoft.VisualStudio.Threading;
using System.Windows.Threading;

namespace FocLauncher
{
    public partial class MainWindow
    {
        private Point _dragPreviewPickupPoint;
        private BlurEffect _overlayArtworkBlur;
        private LauncherProfile? _presetEditorProfile;
        private readonly List<string> _presetEditorArtworkPaths = new List<string>();
        private int _presetEditorArtworkPreviewIndex;
        private bool _presetEditorArtworkChanged;
        private bool _presetEditorArtworkCleared;
        private bool _visualTransitionsAttached;
        private bool _artworkTransitionQueued;
        private bool _themeTransitionQueued;
        private bool _windowLayoutRestored;
        private ImageSource _lastArtworkSource;

        private static readonly DependencyProperty ScrollMaskStateProperty =
            DependencyProperty.RegisterAttached(
                "ScrollMaskState",
                typeof(int),
                typeof(MainWindow),
                new PropertyMetadata(-1));

        public static readonly DependencyProperty IsCompactSidebarProperty = DependencyProperty.Register(
            nameof(IsCompactSidebar), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

        static MainWindow()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(MainWindow), new FrameworkPropertyMetadata(typeof(MainWindow)));
            RuntimeHelpers.RunClassConstructor(typeof(ScrollBarThemingUtilities).TypeHandle);
        }

        public MainWindow()
        {
            InitializeComponent();
        }

        public bool IsCompactSidebar
        {
            get => (bool) GetValue(IsCompactSidebarProperty);
            private set => SetValue(IsCompactSidebarProperty, value);
        }

        private UIElement FindTemplateElement(string name)
        {
            return Template?.FindName(name, this) as UIElement;
        }

        private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            RestoreWindowLayout();
            if (!_visualTransitionsAttached && DataContext is MainWindowViewModel viewModel)
            {
                viewModel.PropertyChanged += MainWindowViewModel_PropertyChanged;
                _visualTransitionsAttached = true;
                _lastArtworkSource = ArtworkSurfaceImage.Source;
            }
            UpdateSidebarMode();
        }

        private void RestoreWindowLayout()
        {
            if (_windowLayoutRestored)
                return;

            _windowLayoutRestored = true;
            var settings = Properties.Settings.Default;
            var workArea = SystemParameters.WorkArea;

            Width = ClampDimension(settings.WindowWidth, MinWidth, workArea.Width);
            Height = ClampDimension(settings.WindowHeight, MinHeight, workArea.Height);

            var sidebarWidth = ClampDimension(settings.SidebarWidth, SidebarColumn.MinWidth, SidebarColumn.MaxWidth);
            SidebarColumn.Width = new GridLength(sidebarWidth);
        }

        private static double ClampDimension(double value, double minimum, double maximum)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
                value = minimum;

            return Math.Max(minimum, Math.Min(value, Math.Max(minimum, maximum)));
        }

        private void MainWindowViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.ActiveGameBackgroundImage) ||
                e.PropertyName == nameof(MainWindowViewModel.UseGameArtwork))
            {
                QueueArtworkTransition();
                return;
            }

            if (e.PropertyName == nameof(MainWindowViewModel.SelectedTheme) ||
                e.PropertyName == nameof(MainWindowViewModel.SelectedThemeName) ||
                e.PropertyName == nameof(MainWindowViewModel.SelectedSubThemeName))
                QueueThemeTransition();
        }

        private void QueueArtworkTransition()
        {
            if (_artworkTransitionQueued)
                return;

            _artworkTransitionQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                _artworkTransitionQueued = false;
                AnimateArtworkTransition();
            }));
        }

        private void AnimateArtworkTransition()
        {
            var viewModel = DataContext as MainWindowViewModel;
            if (viewModel == null)
                return;

            if (!viewModel.UseGameArtwork)
            {
                ArtworkSurfaceTransitionImage.BeginAnimation(UIElement.OpacityProperty, null);
                ArtworkSurfaceTransitionImage.Opacity = 0;
                ArtworkSurfaceTransitionImage.Source = null;
                ArtworkSurfaceImage.BeginAnimation(UIElement.OpacityProperty, null);
                FadeAuxiliaryArtworkImages(false);
                _lastArtworkSource = ArtworkSurfaceImage.Source;
                return;
            }

            var currentSource = ArtworkSurfaceImage.Source;
            var previousSource = _lastArtworkSource;
            _lastArtworkSource = currentSource;
            if (currentSource == null || previousSource == null || Equals(currentSource, previousSource))
            {
                FadeAuxiliaryArtworkImages(true);
                return;
            }

            ArtworkSurfaceTransitionImage.BeginAnimation(UIElement.OpacityProperty, null);
            ArtworkSurfaceTransitionImage.Source = previousSource;
            ArtworkSurfaceTransitionImage.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                });

            ArtworkSurfaceImage.BeginAnimation(UIElement.OpacityProperty, null);
            ArtworkSurfaceImage.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                });

            FadeAuxiliaryArtworkImages(true);
        }

        private void FadeAuxiliaryArtworkImages(bool showArtwork)
        {
            foreach (var image in GetArtworkImages())
            {
                if (ReferenceEquals(image, ArtworkSurfaceImage) ||
                    ReferenceEquals(image, ArtworkSurfaceTransitionImage) ||
                    image.Visibility != Visibility.Visible)
                    continue;

                image.BeginAnimation(UIElement.OpacityProperty, null);
                if (!showArtwork)
                    continue;

                var fadeOut = new DoubleAnimation(image.Opacity, 0, TimeSpan.FromMilliseconds(45))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                };
                fadeOut.Completed += (sender, args) =>
                {
                    image.BeginAnimation(UIElement.OpacityProperty,
                        new DoubleAnimation(0, 0.96, TimeSpan.FromMilliseconds(120))
                        {
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                        });
                };
                image.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
        }

        private List<Image> GetArtworkImages()
        {
            var images = new List<Image>();
            AddArtworkImage(images, FindTemplateElement("ArtworkImage") as Image);
            foreach (var image in FindVisualChildren<Image>(this))
            {
                if (image.Name == "TitleBarArtworkImage")
                    AddArtworkImage(images, image);
            }

            foreach (var backdrop in FindVisualChildren<AcrylicBackdrop>(this))
                AddArtworkImage(images, backdrop.Template?.FindName("BackdropImage", backdrop) as Image);

            return images;
        }

        private static void AddArtworkImage(ICollection<Image> images, Image image)
        {
            if (image != null && !images.Contains(image))
                images.Add(image);
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null)
                yield break;

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            {
                var child = VisualTreeHelper.GetChild(root, index);
                if (child is T match)
                    yield return match;

                foreach (var descendant in FindVisualChildren<T>(child))
                    yield return descendant;
            }
        }

        private void QueueThemeTransition()
        {
            if (_themeTransitionQueued)
                return;

            _themeTransitionQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                _themeTransitionQueued = false;
                AnimateThemeTransition();
            }));
        }

        private void AnimateThemeTransition()
        {
            AnimateThemeElement(LauncherContent);
            AnimateThemeElement(FindTemplateElement("MainWindowTitleBar"));
        }

        private static void AnimateThemeElement(UIElement element)
        {
            if (element == null)
                return;

            element.BeginAnimation(UIElement.OpacityProperty, null);
            var fadeOut = new DoubleAnimation(element.Opacity, 0.82, TimeSpan.FromMilliseconds(70))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fadeOut.Completed += (sender, args) =>
            {
                element.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(160))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                    });
            };
            element.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        private void MainListScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            UpdateScrollMask(e);
        }

        private void AddModsScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            UpdateScrollMask(e);
        }

        private void PresetScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            UpdateScrollMask(e);
        }

        private void SettingsScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            UpdateScrollMask(e);
        }

        private static void UpdateScrollMask(ScrollChangedEventArgs e)
        {
            var scrollViewer = e.OriginalSource as ScrollViewer ?? e.Source as ScrollViewer;
            if (scrollViewer == null && e.Source is DependencyObject source)
            {
                foreach (var candidate in FindVisualChildren<ScrollViewer>(source))
                {
                    scrollViewer = candidate;
                    break;
                }
            }

            if (scrollViewer != null)
                UpdateScrollMask(scrollViewer);
        }

        private static void UpdateScrollMask(ScrollViewer scrollViewer)
        {
            ScrollContentPresenter? presenter = null;
            foreach (var candidate in FindVisualChildren<ScrollContentPresenter>(scrollViewer))
            {
                presenter = candidate;
                break;
            }

            if (presenter == null)
            {
                scrollViewer.Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
                    new Action(() => UpdateScrollMask(scrollViewer)));
                return;
            }

            var maxOffset = Math.Max(0, scrollViewer.ExtentHeight - scrollViewer.ViewportHeight);
            var hasContentAbove = scrollViewer.VerticalOffset > 0.5;
            var hasContentBelow = scrollViewer.VerticalOffset < maxOffset - 0.5;
            var maskState = (hasContentAbove ? 1 : 0) | (hasContentBelow ? 2 : 0);
            var previousState = (int)presenter.GetValue(ScrollMaskStateProperty);
            if (previousState == maskState)
                return;

            presenter.SetValue(ScrollMaskStateProperty, maskState);
            if (maskState == 0)
            {
                presenter.ClearValue(UIElement.OpacityMaskProperty);
                return;
            }

            const double edge = 0.12;
            var mask = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                MappingMode = BrushMappingMode.RelativeToBoundingBox
            };

            if (hasContentAbove)
            {
                mask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
                mask.GradientStops.Add(new GradientStop(Colors.White, edge));
            }
            else
            {
                mask.GradientStops.Add(new GradientStop(Colors.White, 0));
            }

            if (hasContentBelow)
            {
                mask.GradientStops.Add(new GradientStop(Colors.White, 1 - edge));
                mask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
            }
            else
            {
                mask.GradientStops.Add(new GradientStop(Colors.White, 1));
            }

            mask.Freeze();
            presenter.OpacityMask = mask;
        }

        private void MainWindow_OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateSidebarMode();
        }

        private void SidebarSplitter_OnDragCompleted(object sender, DragCompletedEventArgs e)
        {
            UpdateSidebarMode();
        }

        private void UpdateSidebarMode()
        {
            IsCompactSidebar = ActualWidth < 900 || SidebarColumn.ActualWidth < 210;
        }

        private void OpenPresetOverflow(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement element) || element.ContextMenu == null)
                return;

            element.ContextMenu.PlacementTarget = element;
            element.ContextMenu.IsOpen = true;
        }

        private void ProfileNameEditor_OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(sender is TextBox editor) || !(e.NewValue is bool isVisible) || !isVisible)
                return;

            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (editor.IsVisible)
                {
                    editor.Focus();
                    editor.SelectAll();
                }
            }));
        }

        private void ProfileNameEditor_OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!(sender is TextBox editor) || !(editor.DataContext is LauncherProfile profile) ||
                !(DataContext is MainWindowViewModel viewModel))
                return;

            if (e.Key == Key.Enter)
            {
                viewModel.CommitProfileRename(profile);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                viewModel.CancelProfileRename(profile);
                e.Handled = true;
            }
        }

        private void ProfileNameEditor_OnLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox editor && editor.DataContext is LauncherProfile profile &&
                DataContext is MainWindowViewModel viewModel)
                viewModel.CommitProfileRename(profile);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            SetWindowIcon();
        }

        private void SetWindowIcon()
        {
            IconHelper.UseWindowIconAsync(windowIcon => Icon = windowIcon).Forget();
        }

        private void OpenAboutWindow(object sender, RoutedEventArgs e)
        {
            ShowOverlay(AboutOverlay);
        }

        private void OpenNewPresetEditor(object sender, RoutedEventArgs e)
        {
            OpenPresetEditor(null);
        }

        private void OpenEditPresetEditor(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel viewModel && viewModel.ActiveProfile != null)
                OpenPresetEditor(viewModel.ActiveProfile);
        }

        private void OpenPresetEditorFromMenu(object sender, RoutedEventArgs e)
        {
            OpenEditPresetEditor(sender, e);
        }

        private void OpenPresetEditor(LauncherProfile? profile)
        {
            if (profile == null && (!(DataContext is MainWindowViewModel viewModel) || viewModel.ActiveGame == null))
                return;

            _presetEditorProfile = profile;
            _presetEditorArtworkPaths.Clear();
            if (profile != null)
                _presetEditorArtworkPaths.AddRange(profile.GetArtworkPaths());
            _presetEditorArtworkPreviewIndex = 0;
            _presetEditorArtworkChanged = false;
            _presetEditorArtworkCleared = false;
            PresetEditorTitle.Text = profile == null ? "New preset" : "Edit preset";
            PresetEditorNameBox.Text = profile?.Name ?? "New Profile";
            PresetArtworkIntervalBox.Text = (profile?.ArtworkIntervalSeconds > 0
                ? profile.ArtworkIntervalSeconds
                : MainWindowViewModel.DefaultArtworkIntervalSeconds).ToString();
            PresetArtworkValidation.Text = string.Empty;
            PresetArtworkValidation.Visibility = Visibility.Collapsed;
            SetPresetArtworkPreview();
            SetPresetEditorSubTheme(profile?.SubThemeName);
            ShowOverlay(PresetEditorOverlay);
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                SetPresetEditorSubTheme(profile?.SubThemeName);
                PresetEditorNameBox.Focus();
                PresetEditorNameBox.SelectAll();
            }));
        }

        private void SetPresetEditorSubTheme(string? subThemeName)
        {
            var desiredName = string.IsNullOrWhiteSpace(subThemeName)
                ? MainWindowViewModel.InheritPresetSubTheme
                : subThemeName;

            for (var index = 0; index < PresetEditorSubThemeBox.Items.Count; index++)
            {
                if (string.Equals(PresetEditorSubThemeBox.Items[index] as string, desiredName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    PresetEditorSubThemeBox.SelectedIndex = index;
                    return;
                }
            }

            if (PresetEditorSubThemeBox.Items.Count > 0)
                PresetEditorSubThemeBox.SelectedIndex = 0;
        }

        private void ChoosePresetArtwork(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Choose preset artwork images",
                Filter = "Artwork images|*.png;*.jpg;*.jpeg;*.bmp|PNG images|*.png|JPEG images|*.jpg;*.jpeg|Bitmap images|*.bmp",
                CheckFileExists = true,
                Multiselect = true
            };
            if (dialog.ShowDialog(this) != true)
                return;

            var selectedArtworkPaths = new List<string>();
            foreach (var path in dialog.FileNames)
            {
                if (!TryValidatePresetArtwork(path, out var message))
                {
                    PresetArtworkValidation.Text = $"{Path.GetFileName(path)}: {message}";
                    PresetArtworkValidation.Visibility = Visibility.Visible;
                    return;
                }

                if (!selectedArtworkPaths.Exists(existing =>
                        string.Equals(existing, path, StringComparison.OrdinalIgnoreCase)))
                    selectedArtworkPaths.Add(path);
            }

            _presetEditorArtworkPaths.Clear();
            _presetEditorArtworkPaths.AddRange(selectedArtworkPaths);
            _presetEditorArtworkPreviewIndex = 0;
            _presetEditorArtworkChanged = true;
            _presetEditorArtworkCleared = false;
            PresetArtworkValidation.Text = selectedArtworkPaths.Count == 1
                ? "Artwork accepted."
                : $"{selectedArtworkPaths.Count} artwork images accepted. They will rotate automatically.";
            PresetArtworkValidation.Visibility = Visibility.Visible;
            SetPresetArtworkPreview();
        }

        private void ClearPresetArtwork(object sender, RoutedEventArgs e)
        {
            _presetEditorArtworkPaths.Clear();
            _presetEditorArtworkPreviewIndex = 0;
            _presetEditorArtworkChanged = true;
            _presetEditorArtworkCleared = true;
            PresetArtworkValidation.Text = "Custom artwork will be removed.";
            PresetArtworkValidation.Visibility = Visibility.Visible;
            SetPresetArtworkPreview();
        }

        private void SavePresetEditor(object sender, RoutedEventArgs e)
        {
            if (!(DataContext is MainWindowViewModel viewModel))
                return;

            var name = PresetEditorNameBox.Text?.Trim() ?? string.Empty;
            var subThemeName = PresetEditorSubThemeBox.SelectedItem as string
                               ?? MainWindowViewModel.InheritPresetSubTheme;
            if (!int.TryParse(PresetArtworkIntervalBox.Text?.Trim(), out var artworkIntervalSeconds) ||
                artworkIntervalSeconds < 1 || artworkIntervalSeconds > 3600)
            {
                PresetArtworkValidation.Text = "Artwork rotation interval must be between 1 and 3600 seconds.";
                PresetArtworkValidation.Visibility = Visibility.Visible;
                return;
            }

            if (!viewModel.SaveProfileEditor(_presetEditorProfile, name, _presetEditorArtworkPaths,
                    _presetEditorArtworkChanged, _presetEditorArtworkCleared, artworkIntervalSeconds,
                    subThemeName, out var error))
            {
                PresetArtworkValidation.Text = error;
                PresetArtworkValidation.Visibility = Visibility.Visible;
                return;
            }

            CloseOverlay();
        }

        private void SetPresetArtworkPreview()
        {
            PresetArtworkPreview.Source = null;
            PresetArtworkEmptyText.Visibility = Visibility.Visible;
            PresetArtworkClearButton.IsEnabled = _presetEditorArtworkPaths.Count > 0;
            PresetArtworkThumbnailPanel.Children.Clear();

            var validArtworkPaths = new List<string>();
            foreach (var path in _presetEditorArtworkPaths)
            {
                if (File.Exists(path))
                    validArtworkPaths.Add(path);
            }

            if (validArtworkPaths.Count == 0)
                return;

            if (_presetEditorArtworkPreviewIndex >= validArtworkPaths.Count)
                _presetEditorArtworkPreviewIndex = 0;

            var previewImage = LoadPresetArtworkImage(validArtworkPaths[_presetEditorArtworkPreviewIndex]);
            if (previewImage != null)
            {
                PresetArtworkPreview.Source = previewImage;
                PresetArtworkEmptyText.Visibility = Visibility.Collapsed;
            }

            for (var index = 0; index < validArtworkPaths.Count; index++)
            {
                var thumbnailImage = LoadPresetArtworkImage(validArtworkPaths[index]);
                if (thumbnailImage == null)
                    continue;

                var thumbnailButton = new Button
                {
                    Content = new Image
                    {
                        Source = thumbnailImage,
                        Stretch = Stretch.UniformToFill
                    },
                    Tag = validArtworkPaths[index],
                    Width = 68,
                    Height = 48,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0, 0, 6, 0),
                    ToolTip = Path.GetFileName(validArtworkPaths[index]),
                    Style = FindResource("ToolbarButtonStyle") as Style
                };
                thumbnailButton.Click += SelectPresetArtworkPreview;

                var thumbnailCard = new Grid
                {
                    Width = 68,
                    Height = 48,
                    Margin = new Thickness(0, 0, 6, 0),
                    ToolTip = Path.GetFileName(validArtworkPaths[index])
                };
                thumbnailCard.Children.Add(thumbnailButton);

                var deleteButton = new Button
                {
                    Content = "×",
                    Tag = validArtworkPaths[index],
                    Width = 20,
                    Height = 20,
                    Margin = new Thickness(0, 2, 2, 0),
                    Padding = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    ToolTip = "Remove this image",
                    Background = new SolidColorBrush(Color.FromArgb(215, 25, 25, 25)),
                    Foreground = Brushes.White,
                    BorderBrush = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Style = FindResource("ToolbarButtonStyle") as Style
                };
                deleteButton.Click += DeletePresetArtwork;
                thumbnailCard.Children.Add(deleteButton);
                PresetArtworkThumbnailPanel.Children.Add(thumbnailCard);
            }
        }

        private void SelectPresetArtworkPreview(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement element) || !(element.Tag is string selectedPath))
                return;

            for (var index = 0; index < _presetEditorArtworkPaths.Count; index++)
            {
                if (string.Equals(_presetEditorArtworkPaths[index], selectedPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _presetEditorArtworkPreviewIndex = index;
                    SetPresetArtworkPreview();
                    return;
                }
            }
        }

        private void DeletePresetArtwork(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement element) || !(element.Tag is string selectedPath))
                return;

            var artworkIndex = _presetEditorArtworkPaths.FindIndex(path =>
                string.Equals(path, selectedPath, StringComparison.OrdinalIgnoreCase));
            if (artworkIndex < 0)
                return;

            _presetEditorArtworkPaths.RemoveAt(artworkIndex);
            if (_presetEditorArtworkPreviewIndex > artworkIndex)
                _presetEditorArtworkPreviewIndex--;
            if (_presetEditorArtworkPreviewIndex >= _presetEditorArtworkPaths.Count)
                _presetEditorArtworkPreviewIndex = Math.Max(0, _presetEditorArtworkPaths.Count - 1);

            _presetEditorArtworkChanged = true;
            _presetEditorArtworkCleared = _presetEditorArtworkPaths.Count == 0;
            PresetArtworkValidation.Text = _presetEditorArtworkCleared
                ? "Custom artwork will be removed."
                : "Image removed. Save preset to keep this change.";
            PresetArtworkValidation.Visibility = Visibility.Visible;
            SetPresetArtworkPreview();
        }

        private static ImageSource? LoadPresetArtworkImage(string path)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool TryValidatePresetArtwork(string path, out string message)
        {
            message = string.Empty;
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count == 0)
                    {
                        message = "Image contains no usable artwork.";
                        return false;
                    }

                    var frame = decoder.Frames[0];
                    if (frame.PixelWidth == frame.PixelHeight)
                    {
                        message = "Square artwork is not supported. Choose a landscape or portrait image.";
                        return false;
                    }

                    message = $"Artwork accepted: {frame.PixelWidth} × {frame.PixelHeight}.";
                    return true;
                }
            }
            catch (Exception)
            {
                message = "Image could not be opened. Choose a PNG, JPEG, or BMP file.";
                return false;
            }
        }

        private void OpenAddModsOverlay(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.RefreshInstalledMods();
                viewModel.RefreshAvailableMods();
            }
            ShowOverlay(AddModsOverlay);
        }

        private void RefreshInstalledMods(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel viewModel)
                viewModel.RefreshInstalledMods();
        }

        private void AddSelectedModsAndClose(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel viewModel)
                viewModel.AddSelectedMods();
            CloseOverlay();
        }

        private void OpenSettingsWindow(object sender, RoutedEventArgs e)
        {
            ShowOverlay(SettingsOverlay);
        }

        private void ShowOverlay(UIElement overlay)
        {
            ResetOverlayAnimation(SettingsOverlay);
            ResetOverlayAnimation(AboutOverlay);
            ResetOverlayAnimation(AddModsOverlay);
            ResetOverlayAnimation(PresetEditorOverlay);
            SettingsOverlay.Visibility = ReferenceEquals(overlay, SettingsOverlay) ? Visibility.Visible : Visibility.Collapsed;
            AboutOverlay.Visibility = ReferenceEquals(overlay, AboutOverlay) ? Visibility.Visible : Visibility.Collapsed;
            AddModsOverlay.Visibility = ReferenceEquals(overlay, AddModsOverlay) ? Visibility.Visible : Visibility.Collapsed;
            PresetEditorOverlay.Visibility = ReferenceEquals(overlay, PresetEditorOverlay) ? Visibility.Visible : Visibility.Collapsed;
            OverlayHost.Visibility = Visibility.Visible;
            OverlayHost.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

            if (overlay is FrameworkElement overlayElement)
            {
                overlayElement.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
                if (overlayElement.RenderTransform is ScaleTransform scale)
                {
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                        new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(320))
                        {
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                        });
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                        new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(320))
                        {
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                        });
                }
            }

            var blur = new BlurEffect
            {
                Radius = 0,
                RenderingBias = RenderingBias.Quality
            };
            LauncherContent.Effect = blur;
            blur.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(0, 8, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

            var artwork = FindTemplateElement("ArtworkImage");
            if (artwork != null)
            {
                var artworkBlur = new BlurEffect
                {
                    Radius = 0,
                    RenderingBias = RenderingBias.Quality
                };
                _overlayArtworkBlur = artworkBlur;
                artwork.Effect = artworkBlur;
                artworkBlur.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(0, 8, TimeSpan.FromMilliseconds(260))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            }
            OverlayHost.Focus();
        }

        private void CloseOverlay(object sender, RoutedEventArgs e)
        {
            CloseOverlay();
        }

        private void CloseOverlay()
        {
            if (OverlayHost.Visibility != Visibility.Visible)
                return;

            var visibleOverlay = SettingsOverlay.Visibility == Visibility.Visible
                ? SettingsOverlay
                : AboutOverlay.Visibility == Visibility.Visible
                    ? AboutOverlay
                    : AddModsOverlay.Visibility == Visibility.Visible
                        ? AddModsOverlay
                        : PresetEditorOverlay;
            var closeAnimation = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            closeAnimation.Completed += (sender, args) =>
            {
                SettingsOverlay.Visibility = Visibility.Collapsed;
                AboutOverlay.Visibility = Visibility.Collapsed;
                PresetEditorOverlay.Visibility = Visibility.Collapsed;
                OverlayHost.Visibility = Visibility.Collapsed;
                OverlayHost.BeginAnimation(UIElement.OpacityProperty, null);
                LauncherContent.Effect = null;
                ListBoxPane.Focus();
            };

            visibleOverlay.BeginAnimation(UIElement.OpacityProperty, closeAnimation);
            if (visibleOverlay.RenderTransform is ScaleTransform scale)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(1, 0.92, TimeSpan.FromMilliseconds(220))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                    });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(1, 0.92, TimeSpan.FromMilliseconds(220))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                    });
            }

            OverlayHost.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                });
            if (LauncherContent.Effect is BlurEffect blur)
                blur.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                });

            var artwork = FindTemplateElement("ArtworkImage");
            if (artwork != null && _overlayArtworkBlur != null)
            {
                var artworkBlur = _overlayArtworkBlur;
                var artworkAnimation = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                artworkAnimation.Completed += (sender, args) =>
                {
                    if (ReferenceEquals(_overlayArtworkBlur, artworkBlur))
                    {
                        artwork.Effect = null;
                        _overlayArtworkBlur = null;
                    }
                };
                artworkBlur.BeginAnimation(BlurEffect.RadiusProperty, artworkAnimation);
            }
        }

        private static void ResetOverlayAnimation(UIElement overlay)
        {
            overlay.BeginAnimation(UIElement.OpacityProperty, null);
            overlay.Opacity = 0;
            if (overlay.RenderTransform is ScaleTransform scale)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleX = 0.92;
                scale.ScaleY = 0.92;
            }
        }

        internal void ShowModDragPreview(LauncherListBoxItem sourceContainer, Point pickupPoint)
        {
            DragPreview.BeginAnimation(UIElement.OpacityProperty, null);
            DragPreview.Opacity = 0;
            DragPreview.Width = sourceContainer.ActualWidth;
            DragPreview.Height = sourceContainer.ActualHeight;
            DragPreview.Background = new VisualBrush(sourceContainer)
            {
                Stretch = Stretch.Fill,
                Opacity = 0.92
            };

            if (DragPreview.RenderTransform is ScaleTransform scale)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleX = 0.96;
                scale.ScaleY = 0.96;
            }

            DragPreviewLayer.Visibility = Visibility.Visible;
            _dragPreviewPickupPoint = pickupPoint;
            UpdateModDragPreview();
            DragPreview.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            if (DragPreview.RenderTransform is ScaleTransform previewScale)
            {
                previewScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(140))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
                previewScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(140))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
            }
        }

        internal void UpdateModDragPreview()
        {
            var mousePoint = Mouse.GetPosition(LauncherContent);
            Canvas.SetLeft(DragPreview, mousePoint.X - _dragPreviewPickupPoint.X);
            Canvas.SetTop(DragPreview, mousePoint.Y - _dragPreviewPickupPoint.Y);
        }

        internal void HideModDragPreview()
        {
            var fadeOut = new DoubleAnimation(DragPreview.Opacity, 0, TimeSpan.FromMilliseconds(100))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (sender, args) =>
            {
                DragPreviewLayer.Visibility = Visibility.Collapsed;
                DragPreview.ClearValue(Border.BackgroundProperty);
            };
            DragPreview.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        private void OverlayHost_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, OverlayHost))
                CloseOverlay();
        }

        private void MainWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && OverlayHost.Visibility == Visibility.Visible)
            {
                CloseOverlay();
                e.Handled = true;
            }
        }

        private void OpenLicenseSite(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }

        private void OpenRepositorySite(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }

        private void OpenChangeThemeDialog(object sender, RoutedEventArgs e)
        {
            new ChangeThemeDialog(this).ShowDialog();
        }
        
        private void MainWindow_OnClosing(object sender, CancelEventArgs e)
        {
            if (DataContext is MainWindowViewModel viewModel)
                viewModel.PropertyChanged -= MainWindowViewModel_PropertyChanged;

            SaveWindowLayout();
            SteamModNamePersister.Instance.Save();
        }

        private void SaveWindowLayout()
        {
            var settings = Properties.Settings.Default;
            var bounds = WindowState == WindowState.Normal ? new Rect(0, 0, ActualWidth, ActualHeight) : RestoreBounds;

            if (bounds.Width >= MinWidth && !double.IsNaN(bounds.Width) && !double.IsInfinity(bounds.Width))
                settings.WindowWidth = bounds.Width;
            if (bounds.Height >= MinHeight && !double.IsNaN(bounds.Height) && !double.IsInfinity(bounds.Height))
                settings.WindowHeight = bounds.Height;

            var sidebarWidth = SidebarColumn.ActualWidth;
            if (sidebarWidth >= SidebarColumn.MinWidth && !double.IsNaN(sidebarWidth) && !double.IsInfinity(sidebarWidth))
                settings.SidebarWidth = Math.Min(sidebarWidth, SidebarColumn.MaxWidth);

            settings.Save();
        }
    }
}
