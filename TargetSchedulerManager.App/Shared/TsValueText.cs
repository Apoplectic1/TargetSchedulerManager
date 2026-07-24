using System.Globalization;

namespace TargetSchedulerManager.App.Shared;

/// <summary>The one journal-value → display-text rule (review N4 — was spelled in both TsSync and
/// SyncMarks): invariant-culture <see cref="Convert.ToString(object?)"/>, null in → null out. Each display
/// decides its own null spelling at the call site: a push-review line shows the literal "null" (it must
/// show <em>something</em>), a mark tooltip passes null through ("no old value" renders as absence).</summary>
internal static class TsValueText
{
    public static string? From(object? value) =>
        value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
}
