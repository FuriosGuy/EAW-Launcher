using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace FocLauncher.Theming
{
    public sealed class BlueTheme : AbstractTheme
    {
        public override string Name => "Blue";

        public override Uri GetResourceUri() =>
            new Uri("/FocLauncher.Theming;component/BlueTheme.xaml", UriKind.Relative);
    }

    public sealed class DarkGraphiteTheme : AbstractTheme
    {
        public override string Name => "Graphite";

        public override Uri GetResourceUri() =>
            new Uri("/FocLauncher.Theming;component/DarkGraphiteTheme.xaml", UriKind.Relative);
    }

    public sealed class DarkPurpleTheme : AbstractTheme
    {
        public override string Name => "Purple";

        public override Uri GetResourceUri() =>
            new Uri("/FocLauncher.Theming;component/DarkPurpleTheme.xaml", UriKind.Relative);
    }

    public sealed class DarkEmeraldTheme : AbstractTheme
    {
        public override string Name => "Emerald";

        public override Uri GetResourceUri() =>
            new Uri("/FocLauncher.Theming;component/DarkEmeraldTheme.xaml", UriKind.Relative);
    }

    public sealed class DarkGoldTheme : AbstractTheme
    {
        public override string Name => "Gold";

        public override Uri GetResourceUri() =>
            new Uri("/FocLauncher.Theming;component/DarkGoldTheme.xaml", UriKind.Relative);
    }

    public sealed class DarkRedTheme : AbstractTheme
    {
        public override string Name => "Red";

        public override Uri GetResourceUri() =>
            new Uri("/FocLauncher.Theming;component/DarkRedTheme.xaml", UriKind.Relative);
    }

    public sealed class DarkCyanTheme : AbstractTheme
    {
        public override string Name => "Cyan";

        public override Uri GetResourceUri() =>
            new Uri("/FocLauncher.Theming;component/DarkCyanTheme.xaml", UriKind.Relative);
    }

    public sealed class DarkOrangeTheme : AbstractTheme
    {
        public override string Name => "Orange";

        public override Uri GetResourceUri() =>
            new Uri("/FocLauncher.Theming;component/DarkOrangeTheme.xaml", UriKind.Relative);
    }

    public sealed class DarkRoseTheme : AbstractTheme
    {
        public override string Name => "Rose";

        public override Uri GetResourceUri() =>
            new Uri("/FocLauncher.Theming;component/DarkRoseTheme.xaml", UriKind.Relative);
    }

    public sealed class LightSkyTheme : AbstractTheme
    {
        public override string Name => "Sky";

        public override Uri GetResourceUri() =>
            new Uri("/FocLauncher.Theming;component/LightSkyTheme.xaml", UriKind.Relative);
    }

    public sealed class LightMintTheme : AbstractTheme
    {
        public override string Name => "Mint";

        public override Uri GetResourceUri() =>
            new Uri("/FocLauncher.Theming;component/LightMintTheme.xaml", UriKind.Relative);
    }

    public sealed class LightRoseTheme : AbstractTheme
    {
        public override string Name => "Rose";

        public override Uri GetResourceUri() =>
            new Uri("/FocLauncher.Theming;component/LightRoseTheme.xaml", UriKind.Relative);
    }

    public sealed class LightLavenderTheme : AbstractTheme
    {
        public override string Name => "Lavender";

        public override Uri GetResourceUri() =>
            new Uri("/FocLauncher.Theming;component/LightLavenderTheme.xaml", UriKind.Relative);
    }

    public sealed class LightPeachTheme : AbstractTheme
    {
        public override string Name => "Peach";

        public override Uri GetResourceUri() =>
            new Uri("/FocLauncher.Theming;component/LightPeachTheme.xaml", UriKind.Relative);
    }

    public sealed class LightSlateTheme : AbstractTheme
    {
        public override string Name => "Slate";

        public override Uri GetResourceUri() =>
            new Uri("/FocLauncher.Theming;component/LightSlateTheme.xaml", UriKind.Relative);
    }

    public sealed class DarkTheme : AbstractTheme
    {
        public override string Name => "Dark";

        public override Uri GetResourceUri() =>
            new Uri("/FocLauncher.Theming;component/DarkTheme.xaml", UriKind.Relative);
    }

    public sealed class LightTheme : AbstractTheme
    {
        public override string Name => "Light";

        public override Uri GetResourceUri() =>
            new Uri("/FocLauncher.Theming;component/LightTheme.xaml", UriKind.Relative);
    }

    public sealed class SystemTheme : AbstractTheme
    {
        public override string Name => "System";

        public override Uri GetResourceUri() =>
            WindowsThemeDetector.IsDarkMode
                ? new DarkTheme().GetResourceUri()
                : new LightTheme().GetResourceUri();
    }

    internal static class WindowsThemeDetector
    {
        private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        public static bool IsDarkMode
        {
            get
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey))
                    {
                        var value = key?.GetValue("AppsUseLightTheme");
                        if (value != null)
                            return Convert.ToInt32(value) == 0;

                        value = key?.GetValue("SystemUsesLightTheme");
                        return value != null && Convert.ToInt32(value) == 0;
                    }
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    public static class BuiltInThemeCatalog
    {
        public static IReadOnlyList<ITheme> CreateSubThemes(ITheme parentTheme)
        {
            var familyName = parentTheme is SystemTheme
                ? (WindowsThemeDetector.IsDarkMode ? "Dark" : "Light")
                : parentTheme?.Name;

            if (string.Equals(familyName, "Dark", StringComparison.OrdinalIgnoreCase))
            {
                return new ITheme[]
                {
                    new DarkTheme(),
                    new BlueTheme(),
                    new DarkGraphiteTheme(),
                    new DarkPurpleTheme(),
                    new DarkEmeraldTheme(),
                    new DarkGoldTheme(),
                    new DarkRedTheme(),
                    new DarkCyanTheme(),
                    new DarkOrangeTheme(),
                    new DarkRoseTheme()
                };
            }

            if (string.Equals(familyName, "Light", StringComparison.OrdinalIgnoreCase))
            {
                return new ITheme[]
                {
                    new LightTheme(),
                    new LightSkyTheme(),
                    new LightMintTheme(),
                    new LightRoseTheme(),
                    new LightLavenderTheme(),
                    new LightPeachTheme(),
                    new LightSlateTheme()
                };
            }

            return parentTheme == null ? Array.Empty<ITheme>() : new[] { parentTheme };
        }

        public static string GetDisplayName(ITheme theme)
        {
            if (theme is DarkTheme || theme is LightTheme)
                return "Default";
            return theme?.Name ?? string.Empty;
        }

        public static ITheme FindSubTheme(IEnumerable<ITheme> themes, string name)
        {
            if (themes == null || string.IsNullOrWhiteSpace(name))
                return null;

            return themes.FirstOrDefault(theme =>
                string.Equals(theme.Name, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(GetDisplayName(theme), name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
