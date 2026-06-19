using Avalonia.Media;

namespace PatchCord;

/// <summary>
/// Mac-side theme helper: ports the palette hex values from the Windows Theme.cs
/// into Avalonia Color/IBrush equivalents.
/// Full live-switching (chip controls) is a later slice (B3.4).
/// </summary>
internal static class MacTheme
{
    // ── Palette registry (mirrors Theme.Palettes in Windows shell) ────────────

    public static readonly string[] Keys = { "Discord", "Dark", "Light", "HighContrast" };

    public static readonly IReadOnlyDictionary<string, MacPalette> Palettes =
        new Dictionary<string, MacPalette>
        {
            ["Discord"] = new MacPalette
            {
                Label = "Discord",
                Bg = "#1E1F22", Card = "#2B2D31", Card2 = "#313338", Border = "#3A3C42",
                Text = "#F2F3F5", Sub = "#B5BAC1", Accent = "#5865F2", AccentHover = "#4752C4", OnAccent = "#FFFFFF",
                Ghost = "#383A40", GhostHover = "#41434A", On = "#23A55A", OnText = "#FFFFFF", Scroll = "#4E5058",
            },
            ["Dark"] = new MacPalette
            {
                Label = "Dark mode",
                Bg = "#05070C", Card = "#0B0E15", Card2 = "#121726", Border = "#1E2636",
                Text = "#EEF1F7", Sub = "#99A2B5", Accent = "#C8CEDD", AccentHover = "#A3ABBD", OnAccent = "#05070C",
                Ghost = "#121726", GhostHover = "#1B2234", On = "#46B27D", OnText = "#FFFFFF", Scroll = "#29314A",
            },
            ["Light"] = new MacPalette
            {
                Label = "Light",
                Bg = "#E1E3E7", Card = "#FFFFFF", Card2 = "#F4F5F7", Border = "#D2D5DA",
                Text = "#1A1B1E", Sub = "#5C5E66", Accent = "#5865F2", AccentHover = "#4752C4", OnAccent = "#FFFFFF",
                Ghost = "#E6E8EC", GhostHover = "#D8DBE0", On = "#1F9D55", OnText = "#FFFFFF", Scroll = "#C2C6CC",
            },
            ["HighContrast"] = new MacPalette
            {
                Label = "High Contrast",
                Bg = "#070210", Card = "#0B0418", Card2 = "#120824", Border = "#682084",
                Text = "#F4EEFF", Sub = "#B9A3E6", Accent = "#742BD7", AccentHover = "#612D96", OnAccent = "#FFFFFF",
                Ghost = "#0E0619", GhostHover = "#1E1136", On = "#86C25A", OnText = "#0A1505", Scroll = "#5C4A82",
            },
        };

    // ── Semantic badge colors (theme-independent) ─────────────────────────────

    /// Neutral badge — "Other mod" / unknown state.
    public const string BadgeNeutral = "#80848E";

    /// Error badge — Discord is running but not patched.
    public const string BadgeError   = "#F23F43";

    /// Text on the neutral/error badges — always white (their backgrounds are fixed semantic colors).
    public const string BadgeText    = "#FFFFFF";

    /// <summary>
    /// Resolve a palette by key, falling back to Dark.
    /// </summary>
    public static MacPalette Resolve(string? key) =>
        key != null && Palettes.TryGetValue(key, out var p) ? p : Palettes["Dark"];

    /// <summary>
    /// Parse a hex color string (e.g. "#1E1F22") to an Avalonia Color.
    /// </summary>
    public static Color ParseColor(string hex) => Color.Parse(hex);

    /// <summary>
    /// Create a SolidColorBrush from a hex string.
    /// </summary>
    public static SolidColorBrush Brush(string hex) => new(ParseColor(hex));
}

/// <summary>
/// A theme's color set — Mac-side equivalent of the Windows ThemePalette.
/// </summary>
internal sealed class MacPalette
{
    public required string Label, Bg, Card, Card2, Border, Text, Sub,
        Accent, AccentHover, OnAccent, Ghost, GhostHover, On, OnText, Scroll;
}
