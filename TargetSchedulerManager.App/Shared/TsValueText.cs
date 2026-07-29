using System.Globalization;
using Astronomy.Catalog.TargetScheduler;

namespace TargetSchedulerManager.App.Shared;

/// <summary>The one journal-value → display-text rule (review N4 — was spelled in both TsSync and
/// SyncMarks): invariant-culture <see cref="Convert.ToString(object?)"/>, null in → null out. Each display
/// decides its own null spelling at the call site: a push-review line shows the literal "null" (it must
/// show <em>something</em>), a mark tooltip passes null through ("no old value" renders as absence).</summary>
internal static class TsValueText
{
    public static string? From(object? value) =>
        value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);

    /// <summary>Display refinement over <see cref="From"/>'s canonical text for a known field: a sentinel
    /// value renders as its meaning (the schema's <c>SentinelLabel</c> — "template default", "camera
    /// default"), never as the raw −1, which reads as an index or an ID (user obs 2026-07-29). The
    /// convention is the flyout's (DOMAIN → sentinel columns), extended to every old→new display: push
    /// review lines and mark tooltips. DISPLAY ONLY — journal comparison and replay stay on the canonical
    /// text, so what pushes is exactly what was written.</summary>
    public static string? ForField(TsTable table, string column, string? valueText)
    {
        if (valueText is null) return null;
        return TsEditableSchema.Find(table, column) is { Sentinel: double s } f
            && double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            && v == s
            ? f.SentinelLabel ?? "default"
            : valueText;
    }
}
