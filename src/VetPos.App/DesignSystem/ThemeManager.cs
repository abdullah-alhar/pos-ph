using System.Windows;
using System.Windows.Media;

namespace VetPos.App.DesignSystem;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>
/// The app is always light (shops asked for a bright, easy-to-read screen; dark mode was removed).
/// This class only swaps the colour theme (Teal, Blue …) used for buttons, highlights and the menu.
/// </summary>
public sealed class ThemeManager
{
    private ResourceDictionary? _accent;

    /// <summary>The colour themes: name → (main colour, light tint, deep colour used for the loading screen).</summary>
    public static IReadOnlyList<AccentOption> Accents { get; } =
    [
        new("Teal", "#0F8B8D", "#0B5E60", "#5ED6CB"),
        new("Blue", "#1D64DB", "#0F2A52", "#8AB8FF"),
        new("Green", "#16833A", "#123A20", "#7BD99A"),
        new("Purple", "#6D3FD1", "#2A1A4F", "#B9A0FF"),
        new("Rose", "#D0215F", "#4A0E27", "#FF93BC"),
        new("Amber", "#C25E06", "#3D2208", "#FFC27A"),
        new("Graphite", "#3E4C59", "#1F2933", "#A9BCCE"),
    ];

    public string Accent { get; private set; } = "Teal";

    public event EventHandler? Changed;

    public void Initialize(AppTheme _) => Apply();

    /// <summary>Sets the light look (the saved light / dark choice is ignored: there is only light).</summary>
    public void Apply(AppTheme _ = AppTheme.Light)
    {
#pragma warning disable WPF0001 // Fluent ThemeMode is marked experimental
        Application.Current.ThemeMode = ThemeMode.Light;
#pragma warning restore WPF0001
        ApplyAccent(Accent);
    }

    /// <summary>Changes the main colour everywhere at once (all screens use these brushes dynamically).</summary>
    public void ApplyAccent(string name)
    {
        var option = Accents.FirstOrDefault(a => a.Name == name) ?? Accents[0];
        Accent = option.Name;
        var app = Application.Current;
        var merged = app.Resources.MergedDictionaries;
        if (_accent is not null) merged.Remove(_accent);
        _accent = option.Name == "Teal" ? null : Build(option);
        if (_accent is not null) merged.Add(_accent);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static ResourceDictionary Build(AccentOption o)
    {
        Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
        var main = C(o.Main);
        var side = C(o.Sidebar);
        var white = Colors.White;
        var black = Colors.Black;
        static Color Mix(Color a, Color b, double t) => Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        SolidColorBrush B(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
        var soft = Mix(main, white, 0.88);
        var d = new ResourceDictionary
        {
            ["Brush.Accent"] = B(main),
            ["Brush.AccentHover"] = B(Mix(main, black, 0.14)),
            ["Brush.AccentPressed"] = B(Mix(main, black, 0.26)),
            ["Brush.AccentSoft"] = B(soft),
            ["Brush.AccentText"] = B(Mix(main, black, 0.22)),
            // The menu is white; the chosen colour marks the open page.
            ["Brush.SidebarAccent"] = B(soft),
            ["Brush.SidebarPrimary"] = B(main),
            ["Brush.Marigold"] = B(main),
            ["Brush.MarigoldHover"] = B(Mix(main, black, 0.14)),
            ["Brush.Selection"] = new SolidColorBrush(main) { Opacity = 0.25 },
            ["SystemAccentColor"] = main,
        };
        var brand = B(side);
        d["Brush.Brand"] = brand;
        return d;
    }

    /// <summary>Light / dark was removed; kept as a no-op for old shortcuts.</summary>
    public void Toggle() => Apply();
}

/// <summary>One colour theme choice in Settings → Appearance.</summary>
public sealed record AccentOption(string Name, string Main, string Sidebar, string Highlight)
{
    public Brush Swatch { get; } = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Main));
}
