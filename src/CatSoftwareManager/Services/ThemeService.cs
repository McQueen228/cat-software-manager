using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace CatSoftwareManager.Services;

public sealed class ThemeService
{
    public void Apply(string? preference)
    {
        var useDark = preference switch
        {
            "Dark" => true,
            "Light" => false,
            _ => IsSystemDark()
        };

        var palette = useDark ? DarkPalette : LightPalette;
        foreach (var (key, color) in palette)
        {
            Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    private static readonly IReadOnlyDictionary<string, string> LightPalette =
        new Dictionary<string, string>
        {
            ["WindowBackground"] = "#FFF8F7F4",
            ["SurfaceBackground"] = "#FFFFFFFF",
            ["SurfaceAlt"] = "#FFF1F0EC",
            ["BorderColor"] = "#FFE1DED7",
            ["PrimaryText"] = "#FF25231F",
            ["SecondaryText"] = "#FF77736B",
            ["Accent"] = "#FFE77E46",
            ["AccentHover"] = "#FFD96D35",
            ["AccentSoft"] = "#FFFFE9DC",
            ["Success"] = "#FF32805B",
            ["Danger"] = "#FFB54848",
            ["DisabledText"] = "#FFAAA59B",
            ["ButtonBackground"] = "#FFF3F1EC",
            ["ButtonHover"] = "#FFE9E6DF"
        };

    private static readonly IReadOnlyDictionary<string, string> DarkPalette =
        new Dictionary<string, string>
        {
            ["WindowBackground"] = "#FF1C1B19",
            ["SurfaceBackground"] = "#FF252421",
            ["SurfaceAlt"] = "#FF2E2D29",
            ["BorderColor"] = "#FF3C3A35",
            ["PrimaryText"] = "#FFF6F2EA",
            ["SecondaryText"] = "#FFAAA59C",
            ["Accent"] = "#FFF0925D",
            ["AccentHover"] = "#FFFFA36F",
            ["AccentSoft"] = "#FF4A2C20",
            ["Success"] = "#FF77C7A1",
            ["Danger"] = "#FFE18383",
            ["DisabledText"] = "#FF6F6B64",
            ["ButtonBackground"] = "#FF34322D",
            ["ButtonHover"] = "#FF403D37"
        };
}
