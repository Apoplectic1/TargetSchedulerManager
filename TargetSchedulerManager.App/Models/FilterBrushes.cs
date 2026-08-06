using Microsoft.UI.Xaml.Media;

using Windows.UI;

namespace TargetSchedulerManager.App.Models;

/// <summary>The filter-identity palette (openspec filter-colored-rows): the passband/emission hue behind
/// every filter-level row, rendered as a low-alpha full-row wash. Domain constants — distinct from
/// <see cref="TargetSchedulerManager.App.ThemeBrushes"/>, whose charter is resolving SYSTEM theme
/// resources; state color (caution/success/critical) comes from there, identity color from here.
/// <c>L</c> and any code outside the palette are deliberately plain — no fallback hue, no warning.</summary>
internal static class FilterBrushes
{
    /// <summary>The one wash-strength knob, tuned for the dark theme by visual sign-off. The wash paints
    /// over the ListViewItem's hover visual, so it must stay low enough for hover chrome (and the
    /// caution/success pills) to read through.</summary>
    internal const byte WashAlpha = 0x26;

    /// <summary>The wash color for a filter code — exact match on the code the Filter column renders, so
    /// wash and letter can never disagree — or null for L/unknown (plain by design). Hues are
    /// contrast-separated from the natural passband colors (2026-08-05 tune: at wash alpha, luminance
    /// differences vanish, so neighbors split by HUE): O pushed to cyan away from G's green; R to orange
    /// and S to crimson away from H, the pure-red anchor.</summary>
    public static Color? WashColor(string filter) => filter switch
    {
        "O" => Color.FromArgb(WashAlpha, 0, 210, 255),
        "H" => Color.FromArgb(WashAlpha, 255, 0, 15),
        "S" => Color.FromArgb(WashAlpha, 255, 0, 128),
        "B" => Color.FromArgb(WashAlpha, 0, 69, 255),
        "G" => Color.FromArgb(WashAlpha, 0, 255, 61),
        "R" => Color.FromArgb(WashAlpha, 255, 120, 0),
        _ => null,
    };

    private static readonly Dictionary<string, Brush> _cache = [];
    private static Brush? _transparent;

    /// <summary>The row wash brush. Plain rows get a shared transparent brush — never null: the row
    /// template's background is the hit-test surface the hover handlers depend on.</summary>
    public static Brush Wash(string filter)
    {
        if (WashColor(filter) is not Color color)
            return _transparent ??= new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        if (!_cache.TryGetValue(filter, out Brush? brush))
            _cache[filter] = brush = new SolidColorBrush(color);
        return brush;
    }
}
