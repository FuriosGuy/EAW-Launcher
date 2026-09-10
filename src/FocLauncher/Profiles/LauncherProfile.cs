using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace FocLauncher.Profiles
{
    public sealed class LauncherProfile : INotifyPropertyChanged
    {
        private string _name;
        private string _editName;
        private string _gameKey;
        private string _artworkPath;
        private List<string> _artworkPaths;
        private int _artworkIntervalSeconds;
        private string _subThemeName;
        private bool _isEditing;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string Name
        {
            get => _name;
            set
            {
                var name = string.IsNullOrWhiteSpace(value) ? "Unnamed Profile" : value.Trim();
                if (name == _name)
                    return;
                _name = name;
                if (!_isEditing)
                    _editName = name;
                OnPropertyChanged();
            }
        }

        [JsonIgnore]
        public string EditName
        {
            get => _editName;
            set
            {
                if (value == _editName)
                    return;
                _editName = value;
                OnPropertyChanged();
            }
        }

        public string GameKey
        {
            get => _gameKey;
            set
            {
                if (value == _gameKey)
                    return;
                _gameKey = value;
                OnPropertyChanged();
            }
        }

        public string ArtworkPath
        {
            get => _artworkPath;
            set
            {
                if (value == _artworkPath)
                    return;
                _artworkPath = value;
                OnPropertyChanged();
            }
        }

        public List<string> ArtworkPaths
        {
            get => _artworkPaths;
            set
            {
                var artworkPaths = value ?? new List<string>();
                if (ReferenceEquals(artworkPaths, _artworkPaths))
                    return;
                _artworkPaths = artworkPaths;
                OnPropertyChanged();
            }
        }

        public int ArtworkIntervalSeconds
        {
            get => _artworkIntervalSeconds;
            set
            {
                var interval = Math.Max(1, value);
                if (interval == _artworkIntervalSeconds)
                    return;
                _artworkIntervalSeconds = interval;
                OnPropertyChanged();
            }
        }

        public string SubThemeName
        {
            get => _subThemeName;
            set
            {
                var subThemeName = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                if (subThemeName == _subThemeName)
                    return;
                _subThemeName = subThemeName;
                OnPropertyChanged();
            }
        }

        [JsonIgnore]
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (value == _isEditing)
                    return;
                _isEditing = value;
                OnPropertyChanged();
            }
        }

        public List<string> ModKeys { get; set; } = new List<string>();

        public LauncherProfile()
        {
            _name = "Unnamed Profile";
            _editName = _name;
            _gameKey = ProfileGameKey.EmpireAtWar;
            _artworkPath = null;
            _artworkPaths = new List<string>();
            _artworkIntervalSeconds = 10;
            _subThemeName = null;
        }

        public LauncherProfile(string name, string gameKey)
        {
            _name = string.IsNullOrWhiteSpace(name) ? "Unnamed Profile" : name.Trim();
            _editName = _name;
            _gameKey = gameKey;
            _artworkPath = null;
            _artworkPaths = new List<string>();
            _artworkIntervalSeconds = 10;
            _subThemeName = null;
        }

        public List<string> GetArtworkPaths()
        {
            var paths = new List<string>();
            foreach (var path in _artworkPaths ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(path) &&
                    !paths.Contains(path, StringComparer.OrdinalIgnoreCase))
                    paths.Add(path);
            }

            // Keep presets created before multi-artwork support working.
            if (!string.IsNullOrWhiteSpace(_artworkPath) &&
                !paths.Contains(_artworkPath, StringComparer.OrdinalIgnoreCase))
                paths.Insert(0, _artworkPath);

            return paths;
        }

        public bool ContainsMod(string modKey)
        {
            return ModKeys.Contains(modKey, StringComparer.OrdinalIgnoreCase);
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
