using System;
using EawModinfo.Spec;
using FocLauncher.Game;
using FocLauncher.Mods;

namespace FocLauncher.Profiles
{
    internal static class ProfileModKey
    {
        internal static string FromMod(IMod mod)
        {
            if (mod is null)
                throw new ArgumentNullException(nameof(mod));

            if (mod.Type == ModType.Workshops)
                return "workshop:" + mod.Identifier;

            if (mod is IHasDirectory directoryMod)
                return "local:" + directoryMod.Directory.Name;

            return "local:" + mod.Identifier;
        }
    }
}
