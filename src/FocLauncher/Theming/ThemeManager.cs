using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FocLauncher.Mods;
using FocLauncher.Properties;
using Microsoft.Win32;

namespace FocLauncher.Theming
{
    public class ThemeManager : IThemeManager
    {
        private ITheme _theme;
        private ITheme _baseTheme;
        private ITheme _subTheme;

        private readonly ContentControl _mainWindow;
        private ResourceDictionary _activeResourceDictionary;
        private Uri _activeResourceUri;
        private bool _profileSubThemeOverrideActive;
        private ITheme _profileBaseTheme;
        private ITheme _profileSubTheme;

        public static ThemeManager Instance => _instance ?? throw new InvalidOperationException("Theme Manager is not initialized");

        public ObservableCollection<ITheme> Themes { get; }

        public ObservableCollection<ITheme> SubThemes { get; }

        public ITheme BaseTheme
        {
            get => _baseTheme;
            set
            {
                if (value == null)
                    throw new NoNullAllowedException();

                var baseTheme = Themes.FirstOrDefault(theme => theme.Equals(value));
                if (baseTheme == null)
                    return;

                SelectBaseTheme(baseTheme, BuiltInThemeCatalog.GetDisplayName(_subTheme), true);
            }
        }

        public ITheme SubTheme
        {
            get => _subTheme;
            set
            {
                if (value == null)
                    throw new NoNullAllowedException();

                var subTheme = SubThemes.FirstOrDefault(theme => theme.Equals(value));
                if (subTheme == null)
                    return;

                SelectSubTheme(subTheme, true);
            }
        }

        private readonly Dictionary<IMod, ITheme> _modThemeMapping = new Dictionary<IMod, ITheme>();
        private static ThemeManager? _instance;

        public event EventHandler<ThemeChangedEventArgs> ThemeChanged;

        public ITheme Theme
        {
            get => _theme;
            set
            {
                if (value == null)
                    throw new NoNullAllowedException();
                if (Equals(value, _theme))
                    return;

                var baseTheme = FindBaseTheme(value);
                if (baseTheme != null)
                {
                    SelectBaseTheme(baseTheme, BuiltInThemeCatalog.GetDisplayName(value), true);
                    return;
                }

                RegisterTheme(value);
                _baseTheme = value;
                SubThemes.Clear();
                SubThemes.Add(value);
                _subTheme = value;
                ApplyTheme(value, true);
            }
        }

        private ThemeManager(ContentControl mainWindow)
        {
            _mainWindow = mainWindow;
            var systemTheme = new SystemTheme();
            Themes = new ObservableCollection<ITheme>
            {
                systemTheme,
                new DarkTheme(),
                new LightTheme()
            };
            SubThemes = new ObservableCollection<ITheme>();
            _baseTheme = systemTheme;
            RebuildSubThemes();
            _subTheme = SubThemes[0];
            _theme = _subTheme;
            ChangeTheme(null, _theme, false);
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        public void RegisterTheme(ITheme theme)
        {
            if (Themes.Contains(theme))
                return;
            Themes.Add(theme);
        }

        public uint GetThemedColorRgba(ComponentResourceKey componentResourceKey)
        {
            if (componentResourceKey is null)
                return 0;

            var rd = new ResourceDictionary
            {
                Source = Theme.GetResourceUri()
            };

            if (rd.Contains(componentResourceKey))
            {
                var themedObject = rd[componentResourceKey];
                if (themedObject is Color color)
                    return (uint) (color.A << 24 | color.B << 16 | color.G << 8) | color.R;
                if (themedObject is SolidColorBrush colorBrush)
                    return (uint) (colorBrush.Color.A << 24 | colorBrush.Color.B << 16 | colorBrush.Color.G << 8) |
                           colorBrush.Color.R;
            }
            return 0;
        }

        public static void Initialize(ContentControl mainWindow)
        {
            _instance = new ThemeManager(mainWindow);
        }

        public static Uri GetSavedThemeResourceUri()
        {
            var savedTheme = Properties.Settings.Default.DefaultTheme;
            var savedSubTheme = Properties.Settings.Default.DefaultSubTheme;
            if (string.Equals(savedTheme, "Default", StringComparison.OrdinalIgnoreCase))
            {
                savedTheme = "Dark";
                savedSubTheme = "Blue";
            }

            var parentTheme = new ITheme[]
            {
                new SystemTheme(),
                new DarkTheme(),
                new LightTheme()
            }.FirstOrDefault(theme => string.Equals(theme.Name, savedTheme,
                StringComparison.OrdinalIgnoreCase));

            if (parentTheme == null && string.Equals(savedTheme, "Blue", StringComparison.OrdinalIgnoreCase))
            {
                parentTheme = new DarkTheme();
                savedSubTheme = "Blue";
            }

            parentTheme ??= new SystemTheme();
            var subThemes = BuiltInThemeCatalog.CreateSubThemes(parentTheme);
            var subTheme = BuiltInThemeCatalog.FindSubTheme(subThemes, savedSubTheme)
                           ?? subThemes.FirstOrDefault();
            return (subTheme ?? parentTheme).GetResourceUri();
        }

        public static ITheme GetThemeFromFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return null;

            try
            {
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                var assembly = Assembly.LoadFrom(filePath);
                var type = assembly.GetType($"{fileName}.Theme");
                var theme = (ITheme) Activator.CreateInstance(type);
                return theme;
            }
            catch
            {
                return null;
            }
        }

        public void ApplySavedDefaultTheme()
        {
            var savedTheme = Properties.Settings.Default.DefaultTheme;
            var savedSubTheme = Properties.Settings.Default.DefaultSubTheme;

            if (string.IsNullOrWhiteSpace(savedTheme))
                savedTheme = "System";

            // Older builds called the current blue palette "Default" or stored "Blue" directly.
            if (string.Equals(savedTheme, "Default", StringComparison.OrdinalIgnoreCase))
            {
                savedTheme = "Dark";
                savedSubTheme = "Blue";
            }
            else if (string.Equals(savedTheme, "Blue", StringComparison.OrdinalIgnoreCase))
            {
                savedTheme = "Dark";
                savedSubTheme = "Blue";
            }

            var theme = Themes.FirstOrDefault(x => string.Equals(x.Name, savedTheme,
                StringComparison.OrdinalIgnoreCase));
            if (theme != null)
                SelectBaseTheme(theme, savedSubTheme, false);
        }

        public void ApplyProfileSubTheme(string subThemeName)
        {
            if (string.IsNullOrWhiteSpace(subThemeName))
            {
                RestoreProfileSubTheme();
                return;
            }

            var subTheme = BuiltInThemeCatalog.FindSubTheme(SubThemes, subThemeName);
            if (subTheme == null)
            {
                RestoreProfileSubTheme();
                return;
            }

            if (!_profileSubThemeOverrideActive)
            {
                _profileBaseTheme = _baseTheme;
                _profileSubTheme = _subTheme;
                _profileSubThemeOverrideActive = true;
            }

            if (Equals(_theme, subTheme))
                return;

            var oldTheme = _theme;
            _subTheme = subTheme;
            _theme = subTheme;
            ChangeTheme(oldTheme, _theme, false);
            OnThemeChanged(_theme);
        }

        public void RestoreProfileSubTheme()
        {
            if (!_profileSubThemeOverrideActive)
                return;

            var oldTheme = _theme;
            var restoreBaseTheme = _profileBaseTheme ?? _baseTheme;
            var restoreSubTheme = _profileSubTheme;

            _profileSubThemeOverrideActive = false;
            _profileBaseTheme = null;
            _profileSubTheme = null;
            _baseTheme = restoreBaseTheme;
            RebuildSubThemes();
            _subTheme = restoreSubTheme == null
                ? SubThemes.FirstOrDefault() ?? _baseTheme
                : BuiltInThemeCatalog.FindSubTheme(SubThemes, BuiltInThemeCatalog.GetDisplayName(restoreSubTheme))
                  ?? SubThemes.FirstOrDefault()
                  ?? _baseTheme;
            _theme = _subTheme;

            if (Equals(oldTheme, _theme))
            {
                OnThemeChanged(_theme);
                return;
            }

            ChangeTheme(oldTheme, _theme, false);
            OnThemeChanged(_theme);
        }

        private void ChangeTheme(ITheme oldTheme, ITheme theme, bool changeSettings)
        {
            var resources = Application.Current.Resources;
            if (_activeResourceDictionary != null)
            {
                resources.MergedDictionaries.Remove(_activeResourceDictionary);
                _mainWindow?.Resources.MergedDictionaries.Remove(_activeResourceDictionary);
                _activeResourceDictionary = null;
                _activeResourceUri = null;
            }
            else if (oldTheme != null)
            {
                var oldResourceDict = Application.LoadComponent(oldTheme.GetResourceUri()) as ResourceDictionary;
                resources.MergedDictionaries.Remove(oldResourceDict);
                _mainWindow?.Resources.MergedDictionaries.Remove(oldResourceDict);
            }

            if (theme == null)
                return;

            var newResourceDict = Application.LoadComponent(theme.GetResourceUri()) as ResourceDictionary;
            resources.MergedDictionaries.Add(newResourceDict);
            _mainWindow?.Resources.MergedDictionaries.Add(newResourceDict);
            _activeResourceDictionary = newResourceDict;
            _activeResourceUri = theme.GetResourceUri();
            if (changeSettings && Properties.Settings.Default.SaveDefaultTheme)
            {
                Properties.Settings.Default.DefaultTheme = _baseTheme.Name;
                Properties.Settings.Default.DefaultSubTheme = BuiltInThemeCatalog.GetDisplayName(_subTheme);
            }
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (!(_baseTheme is SystemTheme) || e.Category != UserPreferenceCategory.General)
                return;

            Application.Current?.Dispatcher.BeginInvoke(new Action(RefreshSystemTheme));
        }

        private void RefreshSystemTheme()
        {
            if (!(_baseTheme is SystemTheme))
                return;

            SelectBaseTheme(_baseTheme, BuiltInThemeCatalog.GetDisplayName(_subTheme), false);
        }

        private ITheme FindBaseTheme(ITheme theme)
        {
            foreach (var baseTheme in Themes)
            {
                if (baseTheme.Equals(theme))
                    return baseTheme;

                var subTheme = BuiltInThemeCatalog.FindSubTheme(
                    BuiltInThemeCatalog.CreateSubThemes(baseTheme),
                    BuiltInThemeCatalog.GetDisplayName(theme));
                if (subTheme != null && subTheme.Equals(theme))
                    return baseTheme;
            }

            return null;
        }

        private void RebuildSubThemes()
        {
            SubThemes.Clear();
            foreach (var subTheme in BuiltInThemeCatalog.CreateSubThemes(_baseTheme))
                SubThemes.Add(subTheme);
        }

        private void SelectBaseTheme(ITheme baseTheme, string preferredSubThemeName, bool changeSettings)
        {
            var oldTheme = _theme;
            var oldBaseTheme = _baseTheme;
            _baseTheme = baseTheme;
            RebuildSubThemes();

            _subTheme = BuiltInThemeCatalog.FindSubTheme(SubThemes, preferredSubThemeName)
                        ?? SubThemes.FirstOrDefault()
                        ?? baseTheme;

            if (Equals(oldBaseTheme, _baseTheme) && Equals(oldTheme, _subTheme))
                return;

            _theme = _subTheme;
            ChangeTheme(oldTheme, _theme, changeSettings);
            OnThemeChanged(_theme);
        }

        private void SelectSubTheme(ITheme subTheme, bool changeSettings)
        {
            if (Equals(_subTheme, subTheme))
                return;

            var oldTheme = _theme;
            _subTheme = subTheme;
            _theme = subTheme;
            ChangeTheme(oldTheme, _theme, changeSettings);
            OnThemeChanged(_theme);
        }

        private void ApplyTheme(ITheme theme, bool changeSettings)
        {
            var oldTheme = _theme;
            _theme = theme;
            ChangeTheme(oldTheme, theme, changeSettings);
            OnThemeChanged(theme);
        }

        public void AssociateThemeToMod(IMod mod, ITheme theme)
        {
            if (_modThemeMapping.ContainsKey(mod))
                _modThemeMapping[mod] = theme;
            else
                _modThemeMapping.Add(mod, theme);
        }

        public bool TryGetThemeByMod(IMod mod, out ITheme theme)
        {
            theme = default;
            return mod != null && _modThemeMapping.TryGetValue(mod, out theme);
        }

        protected virtual void OnThemeChanged(ITheme theme)
        {
            ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(theme));
        }
    }
}
