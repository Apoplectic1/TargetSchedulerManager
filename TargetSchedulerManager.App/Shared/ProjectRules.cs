using System.Globalization;

namespace TargetSchedulerManager.App.Shared;

/// <summary>
/// TS project cross-field rules the app surfaces as courtesies. TS's Database Manager enforces exactly one at
/// its Save button: a project whose minimum time exceeds twice its meridian window can never be selected for
/// imaging (`ProjectViewVM.Save()` in the TS source). TSM commits per field, so blocking would force an edit
/// order — the rule is evaluated after each relevant commit and surfaced as a warning instead (guards carry
/// facts, buttons carry decisions); the write itself always proceeds.
/// </summary>
internal static class ProjectRules
{
    /// <summary>
    /// True when the (minimum time, meridian window) pair means TS will never select the project:
    /// meridian window in use (&gt; 0) and minimum time &gt; 2 × meridian window. Null or non-numeric values
    /// (nullable columns, absent fields) are simply "no warning" — this is a courtesy, not a contract gate.
    /// </summary>
    public static bool IsNeverSelected(object? minimumTime, object? meridianWindow) =>
        TryNumber(minimumTime, out double minTime)
        && TryNumber(meridianWindow, out double window)
        && window > 0
        && minTime > 2 * window;

    private static bool TryNumber(object? value, out double number)
    {
        if (value is null)
        {
            number = 0;
            return false;
        }
        return double.TryParse(
            Convert.ToString(value, CultureInfo.InvariantCulture),
            NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }
}
