using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EawModinfo.Spec;
using FocLauncher.Game;
using FocLauncher.Game.Language;
using FocLauncher.Utilities;
using HtmlAgilityPack;
using System.Threading.Tasks;

namespace FocLauncher.Mods
{
    public class Mod : ModBase, IHasDirectory
    {
        internal string InternalPath { get; }

        public DirectoryInfo Directory { get; }
        

        //public Mod(IGame game, bool workshop, ModInfoFile modInfoFile) :
        //    base(game, workshop ? ModType.Workshops : ModType.Default, modInfoFile)
        //{
        //}

        public Mod(IGame game, DirectoryInfo modDirectory, bool workshop) :
            this(game, modDirectory, workshop, null)
        {
        }

        public Mod(IGame game, DirectoryInfo modDirectory, bool workshop, IModinfo? modInfoData) :
            base(game, workshop ? ModType.Workshops : ModType.Default, modInfoData)
        {
            if (modDirectory is null)
                throw new ArgumentNullException(nameof(modDirectory));
            if (!modDirectory.Exists)
                throw new PetroglyphModException($"The mod's directory '{modDirectory.FullName}' does not exists.");
            Directory = modDirectory;
            InternalPath = CreateInternalPath(modDirectory);
        }

        public override string Identifier
        {
            get
            {
                switch (Type)
                {
                    case ModType.Default:
                        return InternalPath;
                    case ModType.Workshops: 
                        return Directory.Name;
                    case ModType.Virtual:
                        throw new PetroglyphModException($"Instance of {typeof(Mod)} must not be virtual.");
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        public override bool Equals(IMod other)
        {
            if (other is null)
                return false;
            if (!(other is IHasDirectory directoryMod))
                return false;

            string otherPath;
            if (directoryMod is Mod mod)
                otherPath = mod.InternalPath;
            else
                otherPath = CreateInternalPath(directoryMod.Directory);

            return FileUtilities.Comparer.Equals(InternalPath, otherPath);
        }

        public override bool Equals(IModIdentity other)
        {
            throw new NotImplementedException();
        }

        public override bool Equals(IModReference? modReference)
        {
            if (modReference is null)
                return false;
            if (modReference.Type != Type)
                return false;
            switch (Type)
            {
                case ModType.Default:
                    var realLocation = FileUtilities.NormalizeForPathComparison(modReference.GetAbsolutePath(Game), true);
                    return FileUtilities.Comparer.Equals(InternalPath, realLocation);
                case ModType.Workshops:
                    return Directory.Name.Equals(modReference.Identifier);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public override int GetHashCode()
        {
            var hash = FileUtilities.Comparer.GetHashCode(InternalPath);
            return hash;
        }

        public override string ToArgs(bool includeDependencies)
        {
            if (includeDependencies)
                throw new NotImplementedException();

            var folderName = Directory.Name;
            return WorkshopMod ? $"STEAMMOD={folderName}" : $"MODPATH=Mods/{folderName}"; 
        }

        protected override bool ResolveDependenciesCore()
        {
            throw new NotImplementedException();
        }


        protected override string InitializeName()
        {
            var name = base.InitializeName();
            if (string.IsNullOrEmpty(name))
                name = Directory.Name;
            return name;
        }

        protected override ICollection<ILanguageInfo> ResolveInstalledLanguages()
        {
            var languages =  base.ResolveInstalledLanguages();
            if (!languages.Any())
                languages =  new GenericModLanguageFinder(Directory).Find();
            return languages;
        }

        protected override string? InitializeIcon()
        {
            var declaredIcon = base.InitializeIcon();
            if (!string.IsNullOrEmpty(declaredIcon))
            {
                try
                {
                    var iconPath = Path.Combine(Directory.FullName, declaredIcon);
                    if (File.Exists(iconPath))
                        return iconPath;
                }
                catch (ArgumentException)
                {
                    // Invalid modinfo icon path falls through to file discovery.
                }
            }

            try
            {
                var directIcon = System.IO.Directory.EnumerateFiles(Directory.FullName, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(IsSupportedImageFile)
                    .OrderBy(GetIconPriority)
                    .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (!string.IsNullOrEmpty(directIcon))
                    return directIcon;

                return System.IO.Directory.EnumerateFiles(Directory.FullName, "*.*", SearchOption.AllDirectories)
                    .Where(path => IsSupportedImageFile(path) && IsNamedIconFile(path))
                    .OrderBy(GetIconPriority)
                    .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        internal async Task<string?> ResolveIconFileAsync(HtmlDocument? workshopPage)
        {
            var fileIcon = IconFile;
            if (!WorkshopMod)
                return fileIcon;

            var workshopIcon = await WorkshopImageResolver.ResolveAsync(Identifier, workshopPage).ConfigureAwait(false);
            return workshopIcon ?? fileIcon;
        }

        private static bool IsSupportedImageFile(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".bmp":
                case ".gif":
                case ".ico":
                case ".jpeg":
                case ".jpg":
                case ".png":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsNamedIconFile(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            return name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("logo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("thumb", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int GetIconPriority(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (string.Equals(name, "icon", StringComparison.OrdinalIgnoreCase))
                return 0;
            if (string.Equals(name, "logo", StringComparison.OrdinalIgnoreCase))
                return 1;
            if (name.IndexOf("thumbnail", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("thumb", StringComparison.OrdinalIgnoreCase) >= 0)
                return 2;
            return 3;
        }

        internal static string CreateInternalPath(DirectoryInfo directory)
        {
            return FileUtilities.NormalizeForPathComparison(directory.FullName, true);
        }
    }
}
