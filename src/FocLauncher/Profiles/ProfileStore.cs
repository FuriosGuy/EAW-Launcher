using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace FocLauncher.Profiles
{
    internal sealed class ProfileStore
    {
        private sealed class StoreData
        {
            public List<LauncherProfile> Profiles { get; set; } = new List<LauncherProfile>();
            public Dictionary<string, string> LastProfileIds { get; set; } = new Dictionary<string, string>();
        }

        private const string FileName = "LauncherProfiles.json";
        private readonly object _syncObject = new object();
        private StoreData _data;

        private static ProfileStore? _instance;

        internal static ProfileStore Instance => _instance ??= new ProfileStore();

        internal string FilePath { get; }

        private ProfileStore()
        {
            var applicationBasePath = LauncherConstants.ApplicationBasePath;
            if (string.IsNullOrWhiteSpace(applicationBasePath))
                applicationBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EMPIRE AT WAR Launcher");

            Directory.CreateDirectory(applicationBasePath);
            FilePath = Path.Combine(applicationBasePath, FileName);
            _data = Load();
        }

        internal IList<LauncherProfile> GetProfiles(string gameKey)
        {
            lock (_syncObject)
            {
                return _data.Profiles
                    .Where(profile => string.Equals(profile.GameKey, gameKey, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        internal LauncherProfile Create(string gameKey, string name, IEnumerable<string>? modKeys = null)
        {
            lock (_syncObject)
            {
                var profile = new LauncherProfile(GetUniqueName(gameKey, name), gameKey);
                if (modKeys != null)
                    profile.ModKeys = modKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                _data.Profiles.Add(profile);
                Save();
                return profile;
            }
        }

        internal string ImportArtwork(LauncherProfile profile, string sourcePath)
        {
            if (profile is null)
                throw new ArgumentNullException(nameof(profile));
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                throw new FileNotFoundException("Preset artwork file was not found.", sourcePath);

            lock (_syncObject)
            {
                var artworkDirectory = Path.Combine(Path.GetDirectoryName(FilePath) ?? string.Empty, "ProfileArtwork");
                Directory.CreateDirectory(artworkDirectory);
                var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                var targetPath = Path.Combine(artworkDirectory,
                    profile.Id + "-" + Guid.NewGuid().ToString("N") + extension);
                if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
                    File.Copy(sourcePath, targetPath, true);
                return targetPath;
            }
        }

        internal bool Delete(LauncherProfile profile)
        {
            lock (_syncObject)
            {
                var removed = _data.Profiles.Remove(profile);
                if (removed)
                {
                    if (_data.LastProfileIds.ContainsKey(profile.GameKey) &&
                        _data.LastProfileIds[profile.GameKey] == profile.Id)
                        _data.LastProfileIds.Remove(profile.GameKey);
                    Save();
                }
                return removed;
            }
        }

        internal string? GetLastProfileId(string gameKey)
        {
            lock (_syncObject)
                return _data.LastProfileIds.TryGetValue(gameKey, out var id) ? id : null;
        }

        internal void SetLastProfileId(string gameKey, string profileId)
        {
            lock (_syncObject)
                _data.LastProfileIds[gameKey] = profileId;
        }

        internal void Save()
        {
            var json = JsonConvert.SerializeObject(_data, Formatting.Indented);
            lock (_syncObject)
                File.WriteAllText(FilePath, json);
        }

        private StoreData Load()
        {
            if (!File.Exists(FilePath))
                return new StoreData();

            try
            {
                var json = File.ReadAllText(FilePath);
                return JsonConvert.DeserializeObject<StoreData>(json) ?? new StoreData();
            }
            catch (Exception)
            {
                return new StoreData();
            }
        }

        private string GetUniqueName(string gameKey, string requestedName)
        {
            var baseName = string.IsNullOrWhiteSpace(requestedName) ? "New Profile" : requestedName.Trim();
            var existingNames = new HashSet<string>(
                GetProfiles(gameKey).Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
            if (!existingNames.Contains(baseName))
                return baseName;

            for (var index = 2; index < 10000; index++)
            {
                var candidate = $"{baseName} {index}";
                if (!existingNames.Contains(candidate))
                    return candidate;
            }

            return baseName + " " + Guid.NewGuid().ToString("N").Substring(0, 6);
        }
    }
}
