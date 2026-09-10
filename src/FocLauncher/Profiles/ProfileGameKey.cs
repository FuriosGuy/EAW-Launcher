using System;
using FocLauncher.Game;

namespace FocLauncher.Profiles
{
    internal static class ProfileGameKey
    {
        internal const string EmpireAtWar = "empire-at-war";
        internal const string ForcesOfCorruption = "forces-of-corruption";

        internal static string FromGame(IGame game)
        {
            if (game is null)
                throw new ArgumentNullException(nameof(game));

            return game.Name.IndexOf("Corruption", StringComparison.OrdinalIgnoreCase) >= 0
                ? ForcesOfCorruption
                : EmpireAtWar;
        }
    }
}
