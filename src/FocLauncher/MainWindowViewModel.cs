using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using FocLauncher.Controls;
using FocLauncher.Game;
using FocLauncher.Game.Detection;
using FocLauncher.Input;
using FocLauncher.Items;
using FocLauncher.Profiles;
using FocLauncher.Settings;
using FocLauncher.Theming;
using FocLauncher.Threading;
using Microsoft.VisualStudio.Threading;
using NLog;

namespace FocLauncher
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly ProfileStore _profileStore = ProfileStore.Instance;
        private LauncherProfile? _activeProfile;
        private IGame? _activeGame;
        private bool _applyingProfile;
        private LanguageFallback _languageFallbackOption;
        private LauncherProfile? _renamingProfile;
        private string? _profileRenameOriginalName;
        private string _selectedPlayMode = "WITH MODS";
        private readonly DispatcherTimer _artworkRotationTimer;
        private int _activeArtworkIndex;

        public const int DefaultArtworkIntervalSeconds = 10;

        public sealed class ModPickerEntry : INotifyPropertyChanged
        {
            private bool _isSelected;

            public LauncherItem Item { get; }

            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (value == _isSelected)
                        return;
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }

            public ModPickerEntry(LauncherItem item)
            {
                Item = item;
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<IGame> Games { get; } = new ObservableCollection<IGame>();

        public ObservableCollection<LauncherProfile> Profiles { get; } = new ObservableCollection<LauncherProfile>();

        public ObservableCollection<ModPickerEntry> AvailableMods { get; } =
            new ObservableCollection<ModPickerEntry>();

        public ICollectionView AvailableModsView { get; }

        private string _pickerFilterText = string.Empty;

        public string PickerFilterText
        {
            get => _pickerFilterText;
            set
            {
                value ??= string.Empty;
                if (string.Equals(value, _pickerFilterText, StringComparison.Ordinal))
                    return;
                _pickerFilterText = value;
                AvailableModsView.Refresh();
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPickerFilterEmpty));
            }
        }

        public FocLauncherInformation Launcher { get; } = FocLauncherInformation.Instance;

        public IEnumerable<LanguageFallbackData> LanguageFallbackItems { get; } = new[]
        {
            new LanguageFallbackData(LanguageFallback.NoText, "No text",
                "Use English when a game or mod has no text translation for the system language."),
            new LanguageFallbackData(LanguageFallback.NoFullLocalization, "Not fully localized",
                "Use English when a game or mod is not fully localized for the system language.")
        };

        public IDictionary<FallbackSuppression, string> ExampleEnumsWithCaptions { get; } =
            new Dictionary<FallbackSuppression, string>
            {
                { FallbackSuppression.Always, "Do nothing" },
                { FallbackSuppression.Never, "Update last stable version" },
                { FallbackSuppression.Ask, "Show ask dialog" }
            };

        public Version LauncherVersion => typeof(MainWindowViewModel).Assembly.GetName().Version ?? new Version(1, 0);

        public Version ThemeVersion => typeof(ITheme).Assembly.GetName().Version ?? new Version(1, 0);

        public ObservableCollection<ITheme> Themes => ThemeManager.Instance.Themes;

        public ObservableCollection<ITheme> SubThemes => ThemeManager.Instance.SubThemes;

        public IEnumerable<string> ThemeNames => Themes.Select(theme => theme.Name);

        public IEnumerable<string> PlayModes { get; } = new[] { "WITH MODS", "WITHOUT MODS" };

        public string SelectedPlayMode
        {
            get => _selectedPlayMode;
            set
            {
                if (string.IsNullOrWhiteSpace(value) || string.Equals(value, _selectedPlayMode, StringComparison.Ordinal))
                    return;
                _selectedPlayMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PlayModeGuidance));
                ResetArtworkRotation();
            }
        }

        public string PlayModeGuidance => string.Equals(SelectedPlayMode, "WITHOUT MODS", StringComparison.Ordinal)
            ? "Launches the base game without loading mods."
            : "Launches the selected preset mods in load order.";

        public IEnumerable<string> SubThemeNames => SubThemes.Select(BuiltInThemeCatalog.GetDisplayName);

        public const string InheritPresetSubTheme = "Use current theme";

        public IEnumerable<string> PresetSubThemeNames =>
            new[] { InheritPresetSubTheme }.Concat(SubThemeNames);

        public string SelectedThemeName
        {
            get => ThemeManager.Instance.BaseTheme.Name;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;

                var theme = Themes.FirstOrDefault(candidate => string.Equals(candidate.Name, value,
                    StringComparison.OrdinalIgnoreCase));
                if (theme != null && !Equals(theme, ThemeManager.Instance.BaseTheme))
                    ThemeManager.Instance.BaseTheme = theme;
            }
        }

        public string SelectedSubThemeName
        {
            get => BuiltInThemeCatalog.GetDisplayName(ThemeManager.Instance.SubTheme);
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;

                var subTheme = BuiltInThemeCatalog.FindSubTheme(SubThemes, value);
                if (subTheme != null && !Equals(subTheme, ThemeManager.Instance.SubTheme))
                    ThemeManager.Instance.SubTheme = subTheme;
            }
        }

        public ITheme SelectedTheme
        {
            get => ThemeManager.Instance.Theme;
            set
            {
                if (value == null || Equals(value, ThemeManager.Instance.Theme))
                    return;

                ThemeManager.Instance.Theme = value;
            }
        }

        public bool SaveDefaultTheme
        {
            get => Properties.Settings.Default.SaveDefaultTheme;
            set
            {
                if (value == SaveDefaultTheme)
                    return;

                Properties.Settings.Default.SaveDefaultTheme = value;
                Properties.Settings.Default.Save();
                OnPropertyChanged();
            }
        }

        public LanguageFallback LanguageFallbackOption
        {
            get => _languageFallbackOption;
            set
            {
                if (value == _languageFallbackOption)
                    return;
                _languageFallbackOption = value;
                Properties.Settings.Default.LanguageFallback = value;
                OnPropertyChanged();
            }
        }

        public bool UseBetaBuilds
        {
            get => Launcher.UpdateSearchOption == ApplicationType.Beta;
            set
            {
                if (value == UseBetaBuilds)
                    return;
                Launcher.UpdateSearchOption = value ? ApplicationType.Beta : ApplicationType.Stable;
                OnPropertyChanged();
            }
        }

        public FallbackSuppression UpdateFallbackOption
        {
            get => Launcher.UpdateFallbackSuppression;
            set
            {
                if (value == UpdateFallbackOption)
                    return;
                Launcher.UpdateFallbackSuppression = value;
                OnPropertyChanged();
            }
        }

        private LauncherListBoxPane ListBoxPane { get; }

        public IGame? ActiveGame
        {
            get => _activeGame;
            set
            {
                if (ReferenceEquals(value, _activeGame))
                    return;

                ThemeManager.Instance.RestoreProfileSubTheme();
                _activeGame = value;
                ListBoxPane.ActiveGame = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActiveGameName));
                OnPropertyChanged(nameof(ActiveGameIcon));
                OnPropertyChanged(nameof(ActiveGameStatus));
                OnPropertyChanged(nameof(ActiveGameStore));
                LoadProfilesForActiveGame();
                ResetArtworkRotation();
                RaiseCommandManagerChanged();
            }
        }

        public LauncherProfile? ActiveProfile
        {
            get => _activeProfile;
            set
            {
                if (ReferenceEquals(value, _activeProfile))
                    return;

                _activeProfile = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsVanillaPreset));
                if (ActiveGame != null)
                {
                    if (value != null)
                        _profileStore.SetLastProfileId(ProfileGameKey.FromGame(ActiveGame), value.Id);
                    ApplyActiveProfile();
                    ApplyActiveProfileTheme();
                    RefreshAvailableMods();
                    _profileStore.Save();
                }
                ResetArtworkRotation();
                RaiseCommandManagerChanged();
            }
        }

        public string ActiveGameName => ActiveGame?.Name ?? "Loading games...";

        public string ActiveGameIcon => ActiveGame?.IconFile ?? string.Empty;

        public string ActiveGameBackgroundImage
        {
            get
            {
                var artworkPaths = GetActiveArtworkPaths();
                if (artworkPaths.Count > 0)
                    return artworkPaths[Math.Min(_activeArtworkIndex, artworkPaths.Count - 1)];

                return ActiveGame?.Name.IndexOf("Forces of Corruption", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "/FocLauncher;component/Resources/Art/foc-art.jpg"
                    : "/FocLauncher;component/Resources/Art/eaw-art.jpg";
            }
        }

        public bool UseGameArtwork
        {
            get => Properties.Settings.Default.UseGameArtwork;
            set
            {
                if (value == UseGameArtwork)
                    return;
                Properties.Settings.Default.UseGameArtwork = value;
                Properties.Settings.Default.Save();
                OnPropertyChanged();
                ResetArtworkRotation();
            }
        }

        public string ActiveGameStatus => ActiveGame?.Exists() == true ? "Installed" : "Not found";

        public string ActiveGameStore => ActiveGame?.Type == GameType.SteamGold ? "Steam Gold Pack" : "Local installation";

        public string SelectedModSummary
        {
            get
            {
                var count = ListBoxPane.GetSelectedModCount();
                return count == 1 ? "1 mod selected" : $"{count} mods selected";
            }
        }

        public bool IsPresetEmpty => ActiveGame != null && ListBoxPane.GetPresetModsForActiveGame().Count == 0;

        public bool IsVanillaPreset => ActiveProfile != null &&
                                       string.Equals(ActiveProfile.Name, "Vanilla", StringComparison.OrdinalIgnoreCase);

        public bool IsAddPickerEmpty => AvailableMods.Count == 0;

        public bool IsPickerFilterEmpty => AvailableMods.Count > 0 && AvailableModsView.IsEmpty;

        public string LaunchArguments
        {
            get
            {
                if (ActiveGame is null)
                    return "Select a game to preview launch arguments.";

                var selectedMods = ListBoxPane.GetLoadSelectedMods(ActiveGame);
                var args = new GameCommandArguments
                {
                    Mods = selectedMods.Any() ? selectedMods.ToList() : null
                };
                LauncherGameOptions.Instance.FillArgs(args);
                args.Language = LauncherGameOptions.Instance.GetLanguageFromOptions(ActiveGame);
                return args.ToArgs();
            }
        }

        public ICommand LaunchCommand => new UICommand(ExecutedLaunch, CanExecuteLaunch);

        public ICommand NewProfileCommand => new UICommand(CreateProfile, () => ActiveGame != null);

        public ICommand DuplicateProfileCommand => new UICommand(DuplicateProfile, () => ActiveProfile != null);

        public ICommand RenameProfileCommand => new UICommand(BeginProfileRename, () => ActiveProfile != null);

        public ICommand DeleteProfileCommand => new UICommand(DeleteProfile, () => ActiveProfile != null);

        public ICommand RemoveModFromPresetCommand => new UICommand(RemoveModFromPreset, CanRemoveModFromPreset);

        public MainWindowViewModel(MainWindow window)
        {
            _languageFallbackOption = Properties.Settings.Default.LanguageFallback;
            _artworkRotationTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(DefaultArtworkIntervalSeconds)
            };
            _artworkRotationTimer.Tick += ArtworkRotationTimer_OnTick;
            AvailableModsView = new ListCollectionView(AvailableMods);
            AvailableModsView.Filter = IsAvailableModVisible;
            ThemeManager.Instance.ThemeChanged += OnThemeChanged;
            ListBoxPane = window.ListBoxPane;
            ListBoxPane.ModStateChanged += OnModStateChanged;
            ListBoxPane.Focus();
            InitializeLauncherWindowAsync().ForgetButThrow();
        }

        private List<string> GetActiveArtworkPaths()
        {
            if (!string.Equals(SelectedPlayMode, "WITHOUT MODS", StringComparison.Ordinal))
            {
                var customArtwork = ActiveProfile?.GetArtworkPaths()
                    .Where(path => File.Exists(path))
                    .ToList();
                if (customArtwork != null && customArtwork.Count > 0)
                    return customArtwork;
            }

            return new List<string>
            {
                ActiveGame?.Name.IndexOf("Forces of Corruption", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "/FocLauncher;component/Resources/Art/foc-art.jpg"
                    : "/FocLauncher;component/Resources/Art/eaw-art.jpg"
            };
        }

        private void ResetArtworkRotation()
        {
            _activeArtworkIndex = 0;
            _artworkRotationTimer.Stop();

            var artworkPaths = GetActiveArtworkPaths();
            if (artworkPaths.Count > 1)
            {
                var interval = ActiveProfile?.ArtworkIntervalSeconds ?? DefaultArtworkIntervalSeconds;
                _artworkRotationTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, interval));
                _artworkRotationTimer.Start();
            }

            OnPropertyChanged(nameof(ActiveGameBackgroundImage));
        }

        private void ArtworkRotationTimer_OnTick(object sender, EventArgs e)
        {
            var artworkPaths = GetActiveArtworkPaths();
            if (artworkPaths.Count < 2)
            {
                _artworkRotationTimer.Stop();
                return;
            }

            _activeArtworkIndex = (_activeArtworkIndex + 1) % artworkPaths.Count;
            OnPropertyChanged(nameof(ActiveGameBackgroundImage));
        }

        private async Task InitializeLauncherWindowAsync()
        {
            await ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await TaskScheduler.Default;
                var gameDetection = await FindGamesAsync();
                if (gameDetection.IsError)
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    CloseApplication(gameDetection);
                    return;
                }

                var gameManager = LauncherGameManager.Instance;
                gameManager.Initialize(gameDetection);
                var foc = gameManager.ForcesOfCorruption!;
                var eaw = gameManager.EmpireAtWar!;

                // Add game roots before discovery so mod collection events have a visible parent.
                await ListBoxPane.AddGameAsync(eaw, false);
                await ListBoxPane.AddGameAsync(foc, false);

                await Task.Run(() =>
                {
                    eaw.Setup(GameSetupOptions.ResolveModDependencies);
                    foc.Setup(GameSetupOptions.ResolveModDependencies);
                });

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                Games.Clear();
                Games.Add(eaw);
                Games.Add(foc);
                ActiveGame = foc;
                ListBoxPane.Focus();
                NotifyModStateChanged();
            });
        }

        private static async Task<GameDetection> FindGamesAsync()
        {
            try
            {
                var gameDetection = GameDetection.NotInstalled;
                await Task.Run(() =>
                {
                    gameDetection = GameDetectionHelper.GetGameInstallations();
                }).ConfigureAwait(false);
                return gameDetection;
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Failed to initialize MainWindow view model: {e.Message}");
                throw;
            }
        }

        private void LoadProfilesForActiveGame()
        {
            Profiles.Clear();
            _activeProfile = null;
            OnPropertyChanged(nameof(ActiveProfile));
            ApplyActiveProfile();
            ApplyActiveProfileTheme();
            RefreshAvailableMods();

            if (ActiveGame is null)
                return;

            var gameKey = ProfileGameKey.FromGame(ActiveGame);
            var profiles = _profileStore.GetProfiles(gameKey);
            foreach (var profile in profiles.Where(profile =>
                         !string.Equals(profile.Name, "Vanilla", StringComparison.OrdinalIgnoreCase)))
                Profiles.Add(profile);

            var lastProfileId = _profileStore.GetLastProfileId(gameKey);
            var selectedProfile = Profiles.FirstOrDefault(profile => profile.Id == lastProfileId);
            if (selectedProfile != null)
                ActiveProfile = selectedProfile;
        }

        private void ApplyActiveProfile()
        {
            _applyingProfile = true;
            try
            {
                ListBoxPane.ApplyProfile(ActiveProfile?.ModKeys ?? new System.Collections.Generic.List<string>());
            }
            finally
            {
                _applyingProfile = false;
            }

            NotifyModStateChanged();
        }

        private void ApplyActiveProfileTheme()
        {
            ThemeManager.Instance.ApplyProfileSubTheme(ActiveProfile?.SubThemeName);
        }

        private void SaveActiveProfile()
        {
            if (_applyingProfile || ActiveProfile is null || ActiveGame is null)
                return;

            ActiveProfile.ModKeys = ListBoxPane.GetPresetModKeys().ToList();
            _profileStore.SetLastProfileId(ProfileGameKey.FromGame(ActiveGame), ActiveProfile.Id);
            _profileStore.Save();
            NotifyModStateChanged();
        }

        internal void RefreshAvailableMods()
        {
            AvailableMods.Clear();
            if (ActiveGame != null && ActiveProfile != null)
            {
                foreach (var item in ListBoxPane.GetAvailableModsForActiveGame(ActiveProfile.ModKeys))
                    AvailableMods.Add(new ModPickerEntry(item));
            }

            AvailableModsView.Refresh();
            OnPropertyChanged(nameof(IsAddPickerEmpty));
            OnPropertyChanged(nameof(IsPickerFilterEmpty));
        }

        internal void RefreshInstalledMods()
        {
            if (ActiveGame is null)
                return;

            try
            {
                ActiveGame.GetPhysicalMods(true);
                RefreshAvailableMods();
                NotifyModStateChanged();
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to refresh installed mods: {0}", exception.Message);
            }
        }

        private bool IsAvailableModVisible(object value)
        {
            if (!(value is ModPickerEntry entry))
                return false;

            if (string.IsNullOrWhiteSpace(PickerFilterText))
                return true;

            var filter = PickerFilterText.Trim();
            return entry.Item.Text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   entry.Item.ModKey.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   entry.Item.ModSource.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal void AddSelectedMods()
        {
            if (ActiveProfile is null)
                return;

            var keys = ActiveProfile.ModKeys.ToList();
            foreach (var entry in AvailableMods.Where(entry => entry.IsSelected))
            {
                if (!string.IsNullOrWhiteSpace(entry.Item.ModKey) &&
                    !keys.Contains(entry.Item.ModKey, StringComparer.OrdinalIgnoreCase))
                    keys.Add(entry.Item.ModKey);
            }

            ActiveProfile.ModKeys = keys;
            ListBoxPane.ApplyProfile(keys);
            _profileStore.Save();
            RefreshAvailableMods();
            NotifyModStateChanged();
        }

        private void CreateProfile()
        {
            if (ActiveGame is null)
                return;

            var profile = _profileStore.Create(
                ProfileGameKey.FromGame(ActiveGame),
                "New Profile",
                Array.Empty<string>());
            Profiles.Add(profile);
            ActiveProfile = profile;
        }

        internal bool SaveProfileEditor(LauncherProfile? profileToEdit, string name,
            IEnumerable<string> artworkSourcePaths, bool artworkChanged, bool artworkCleared,
            int artworkIntervalSeconds, string subThemeName, out string error)
        {
            error = string.Empty;
            if (ActiveGame is null)
            {
                error = "Select a game before creating a preset.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                error = "Enter a preset name.";
                return false;
            }

            if (artworkIntervalSeconds < 1 || artworkIntervalSeconds > 3600)
            {
                error = "Artwork rotation interval must be between 1 and 3600 seconds.";
                return false;
            }

            try
            {
                var selectedSubThemeName = string.Equals(subThemeName, InheritPresetSubTheme,
                    StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(subThemeName)
                    ? null
                    : subThemeName.Trim();
                var profile = profileToEdit;
                if (profile is null)
                {
                    profile = _profileStore.Create(ProfileGameKey.FromGame(ActiveGame), name, Array.Empty<string>());
                    profile.SubThemeName = selectedSubThemeName;
                    Profiles.Add(profile);
                    ActiveProfile = profile;
                }
                else
                {
                    profile.Name = name.Trim();
                    profile.SubThemeName = selectedSubThemeName;
                }

                profile.ArtworkIntervalSeconds = artworkIntervalSeconds;
                if (artworkCleared)
                {
                    profile.ArtworkPaths = new List<string>();
                    profile.ArtworkPath = null;
                }
                else if (artworkChanged)
                {
                    var importedArtworkPaths = new List<string>();
                    foreach (var sourcePath in artworkSourcePaths ?? Enumerable.Empty<string>())
                    {
                        if (string.IsNullOrWhiteSpace(sourcePath) ||
                            importedArtworkPaths.Contains(sourcePath, StringComparer.OrdinalIgnoreCase))
                            continue;

                        importedArtworkPaths.Add(_profileStore.ImportArtwork(profile, sourcePath));
                    }

                    profile.ArtworkPaths = importedArtworkPaths;
                    profile.ArtworkPath = importedArtworkPaths.FirstOrDefault();
                }

                _profileStore.Save();
                ResetArtworkRotation();
                ApplyActiveProfileTheme();
                NotifyModStateChanged();
                RaiseCommandManagerChanged();
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to save preset details: {0}", exception.Message);
                error = "Preset could not be saved. Check the artwork file and try again.";
                return false;
            }
        }

        private void DuplicateProfile()
        {
            if (ActiveGame is null || ActiveProfile is null)
                return;

            SaveActiveProfile();
            var profile = _profileStore.Create(
                ProfileGameKey.FromGame(ActiveGame),
                ActiveProfile.Name + " Copy",
                ActiveProfile.ModKeys);
            Profiles.Add(profile);
            ActiveProfile = profile;
        }

        private void BeginProfileRename()
        {
            if (ActiveProfile is null)
                return;

            if (_renamingProfile != null)
                CommitProfileRename(_renamingProfile);

            _renamingProfile = ActiveProfile;
            _profileRenameOriginalName = ActiveProfile.Name;
            ActiveProfile.EditName = ActiveProfile.Name;
            ActiveProfile.IsEditing = true;
        }

        internal void CommitProfileRename(LauncherProfile profile)
        {
            if (!ReferenceEquals(profile, _renamingProfile))
                return;

            profile.Name = profile.EditName;
            profile.IsEditing = false;
            _renamingProfile = null;
            _profileRenameOriginalName = null;
            _profileStore.Save();
        }

        internal void CancelProfileRename(LauncherProfile profile)
        {
            if (!ReferenceEquals(profile, _renamingProfile))
                return;

            profile.EditName = _profileRenameOriginalName ?? profile.Name;
            profile.IsEditing = false;
            _renamingProfile = null;
            _profileRenameOriginalName = null;
        }

        private void DeleteProfile()
        {
            if (ActiveGame is null || ActiveProfile is null)
                return;

            var profile = ActiveProfile;
            _profileStore.Delete(profile);
            Profiles.Remove(profile);
            ActiveProfile = Profiles.FirstOrDefault();
            _profileStore.Save();
        }

        private void RemoveModFromPreset(object parameter)
        {
            if (ActiveProfile is null || !(parameter is LauncherItem item))
                return;

            var index = ActiveProfile.ModKeys.FindIndex(key =>
                string.Equals(key, item.ModKey, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return;

            ActiveProfile.ModKeys.RemoveAt(index);
            ListBoxPane.ApplyProfile(ActiveProfile.ModKeys);
            _profileStore.Save();
            RefreshAvailableMods();
            NotifyModStateChanged();
            RaiseCommandManagerChanged();
        }

        private bool CanRemoveModFromPreset(object parameter)
        {
            return ActiveProfile != null && parameter is LauncherItem item && ActiveProfile.ContainsMod(item.ModKey);
        }

        private void OnModStateChanged(object sender, EventArgs e)
        {
            SaveActiveProfile();
        }

        private void OnThemeChanged(object sender, ThemeChangedEventArgs e)
        {
            OnPropertyChanged(nameof(SelectedTheme));
            OnPropertyChanged(nameof(SelectedThemeName));
            OnPropertyChanged(nameof(ThemeNames));
            OnPropertyChanged(nameof(SubThemeNames));
            OnPropertyChanged(nameof(PresetSubThemeNames));
            OnPropertyChanged(nameof(SelectedSubThemeName));
        }

        private void NotifyModStateChanged()
        {
            OnPropertyChanged(nameof(SelectedModSummary));
            OnPropertyChanged(nameof(LaunchArguments));
            OnPropertyChanged(nameof(IsPresetEmpty));
            OnPropertyChanged(nameof(IsVanillaPreset));
            OnPropertyChanged(nameof(IsPickerFilterEmpty));
        }

        private void ExecutedLaunch()
        {
            if (ActiveGame is null)
                return;

            IReadOnlyList<IPetroglyhGameableObject> selectedMods;
            if (string.Equals(SelectedPlayMode, "WITHOUT MODS", StringComparison.Ordinal))
                selectedMods = Array.Empty<IPetroglyhGameableObject>();
            else
                selectedMods = ListBoxPane.GetLoadSelectedMods(ActiveGame).Cast<IPetroglyhGameableObject>().ToList();
            LauncherGameObjectCommandHandler.Launch(ActiveGame, selectedMods.Any() ? selectedMods : null);
        }

        private bool CanExecuteLaunch()
        {
            return ActiveGame != null;
        }

        private static void RaiseCommandManagerChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }

        private static void CloseApplication(GameDetection gameDetection)
        {
            var message = string.Empty;
            if (gameDetection.EawExe == null || !gameDetection.EawExe.Exists)
                message = "Could not find Empire at War!\r\n";
            else if (gameDetection.FocExe == null || !gameDetection.FocExe.Exists)
                message += "Could not find Forces of Corruption\r\n";
            MessageBox.Show(message + "\r\nThe launcher will now be closed", "EMPIRE AT WAR Launcher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Application.Current.Shutdown();
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
