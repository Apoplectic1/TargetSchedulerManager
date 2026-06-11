using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace TargetSchedulerManager.App.ViewModels.Rows;

/// <summary>
/// The soft system fills behind delta cells, resolved from the app's theme resources. One convention
/// grid-wide: caution = the target/filter still needs telescope time (or its sub lengths drifted),
/// success = the plan's goals are met. Defensive lookup — a missing key means no fill, never a crash.
/// </summary>
internal static class ThemeBrushes
{
    public static Brush? Caution => Lookup("SystemFillColorCautionBackgroundBrush");
    public static Brush? Success => Lookup("SystemFillColorSuccessBackgroundBrush");

    /// <summary>Error fill — data that shouldn't exist (e.g. a plan whose desired count is 0).</summary>
    public static Brush? Critical => Lookup("SystemFillColorCriticalBackgroundBrush");

    private static Brush? Lookup(string key)
    {
        IDictionary<object, object> resources = Application.Current.Resources;
        return resources.TryGetValue(key, out object? value) ? value as Brush : null;
    }
}
