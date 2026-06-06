using Microsoft.Win32;

namespace VlessMonitor.UI;

/// <summary>A full Fluent 2 colour set for one mode (light or dark).</summary>
public sealed class Palette
{
    public required bool IsDark { get; init; }
    public required Color WindowBg { get; init; }   // mica base / form background
    public required Color CardBg { get; init; }      // layer / card fill
    public required Color CardStroke { get; init; }  // 1px card border
    public required Color ControlFill { get; init; } // inputs, subtle fill
    public required Color ControlHover { get; init; }
    public required Color TextPrimary { get; init; }
    public required Color TextSecondary { get; init; }
    public required Color Accent { get; init; }
    public required Color AccentText { get; init; }  // text/icon on accent
    public required Color Success { get; init; }
    public required Color Caution { get; init; }
    public required Color Critical { get; init; }
}

/// <summary>
/// Theme manager: detects the Windows light/dark mode + accent colour,
/// exposes the active <see cref="Palette"/>, and raises <see cref="Changed"/>
/// when the system theme switches.
/// </summary>
public static class Theme
{
    public static event Action? Changed;

    public static Palette Current { get; private set; } = BuildDark(DefaultAccentDark);

    private static readonly Color DefaultAccentDark = Color.FromArgb(0x60, 0xCD, 0xFF);
    private static readonly Color DefaultAccentLight = Color.FromArgb(0x00, 0x67, 0xC0);

    public static void Refresh()
    {
        bool dark = SystemUsesDarkMode();
        var accent = SystemAccentColor(dark);
        Current = dark ? BuildDark(accent) : BuildLight(accent);
    }

    /// <summary>Re-read system theme; raise Changed if anything differs.</summary>
    public static void ReloadAndNotify()
    {
        var before = Current;
        Refresh();
        if (before.IsDark != Current.IsDark ||
            before.Accent != Current.Accent)
        {
            Changed?.Invoke();
        }
    }

    // ── System probes ────────────────────────────────────────
    public static bool SystemUsesDarkMode()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var v = k?.GetValue("AppsUseLightTheme");
            if (v is int i) return i == 0;
        }
        catch { }
        return true; // default dark
    }

    private static Color SystemAccentColor(bool dark)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            // AccentColor is a DWORD stored as 0xAABBGGRR
            if (k?.GetValue("AccentColor") is int abgr)
            {
                int r = abgr & 0xFF;
                int g = (abgr >> 8) & 0xFF;
                int b = (abgr >> 16) & 0xFF;
                var c = Color.FromArgb(r, g, b);
                return dark ? Lighten(c, 0.30f) : c; // brighten accent for dark bg
            }
        }
        catch { }
        return dark ? DefaultAccentDark : DefaultAccentLight;
    }

    // ── Palette builders (Fluent 2 neutrals) ─────────────────
    private static Palette BuildDark(Color accent) => new()
    {
        IsDark = true,
        WindowBg     = Color.FromArgb(0x20, 0x20, 0x20),
        CardBg       = Color.FromArgb(0x2B, 0x2B, 0x2B),
        CardStroke   = Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF), // ~10% white
        ControlFill  = Color.FromArgb(0x33, 0x33, 0x33),
        ControlHover = Color.FromArgb(0x3D, 0x3D, 0x3D),
        TextPrimary  = Color.FromArgb(0xF2, 0xF2, 0xF2),
        TextSecondary= Color.FromArgb(0x9D, 0x9D, 0x9D),
        Accent       = accent,
        AccentText   = Color.FromArgb(0x10, 0x10, 0x10),
        Success      = Color.FromArgb(0x6C, 0xCB, 0x5F),
        Caution      = Color.FromArgb(0xFF, 0xC8, 0x3D),
        Critical     = Color.FromArgb(0xFF, 0x99, 0xA4),
    };

    private static Palette BuildLight(Color accent) => new()
    {
        IsDark = false,
        WindowBg     = Color.FromArgb(0xF3, 0xF3, 0xF3),
        CardBg       = Color.FromArgb(0xFB, 0xFB, 0xFB),
        CardStroke   = Color.FromArgb(0x12, 0x00, 0x00, 0x00), // ~7% black
        ControlFill  = Color.FromArgb(0xFF, 0xFF, 0xFF),
        ControlHover = Color.FromArgb(0xED, 0xED, 0xED),
        TextPrimary  = Color.FromArgb(0x1A, 0x1A, 0x1A),
        TextSecondary= Color.FromArgb(0x5E, 0x5E, 0x5E),
        Accent       = accent,
        AccentText   = Color.White,
        Success      = Color.FromArgb(0x0F, 0x7B, 0x0F),
        Caution      = Color.FromArgb(0x9D, 0x5D, 0x00),
        Critical     = Color.FromArgb(0xC4, 0x2B, 0x1C),
    };

    private static Color Lighten(Color c, float amt) => Color.FromArgb(
        c.A,
        (int)(c.R + (255 - c.R) * amt),
        (int)(c.G + (255 - c.G) * amt),
        (int)(c.B + (255 - c.B) * amt));

    // ── Backward-compatible static accessors (used across the UI) ──
    public static Color Bg       => Current.WindowBg;
    public static Color Bg2      => Current.CardBg;
    public static Color Bg3      => Current.ControlFill;
    public static Color Fg       => Current.TextPrimary;
    public static Color FgDim    => Current.TextSecondary;
    public static Color Green    => Current.Success;
    public static Color Yellow   => Current.Caution;
    public static Color Red      => Current.Critical;
    public static Color Blue     => Current.Accent;
    public static Color Lavender => Current.Accent;

    public const string FontFamily = "Segoe UI Variable Text";
    public const string FontFamilyFallback = "Segoe UI";

    /// <summary>Returns "Segoe UI Variable Text" if installed, else "Segoe UI".</summary>
    public static string UiFont
    {
        get
        {
            if (_uiFont != null) return _uiFont;
            try
            {
                using var f = new Font(FontFamily, 9f);
                _uiFont = f.Name.StartsWith("Segoe UI Variable") ? FontFamily : FontFamilyFallback;
            }
            catch { _uiFont = FontFamilyFallback; }
            return _uiFont;
        }
    }
    private static string? _uiFont;
}
