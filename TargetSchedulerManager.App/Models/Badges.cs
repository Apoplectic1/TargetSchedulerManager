namespace TargetSchedulerManager.App.Models;

/// <summary>
/// The match-state badge vocabulary (openspec badge-severity-color): the token spellings, the separator that
/// joins them, and the two-tier severity that colours them — defined once and consumed by the loader, the
/// header rollup, and the renderer. The token strings are a soft contract: they are what the grid's search box
/// matches on (<c>ReconciliationRow.Matches</c>), so a rename changes the user's search vocabulary.
/// <para><b>Severity:</b> WARNING = authoring the user must repair outside TSM (by hand in NINA's TS UI);
/// INFORMATIVE = a fact carrying no call to action. The warning set is deliberately the same set that sets
/// <c>IsFlagged</c> and drives the flagged-only filter — colour and filter must never disagree.</para>
/// </summary>
internal static class Badges
{
    /// <summary>Joins tokens within one cell — the same " · " the identity labels use, kept separate because
    /// this one is split back apart by the renderer.</summary>
    public const string Separator = " · ";

    // ---- Informative: facts, nothing to fix. ------------------------------------------------------------
    /// <summary>The target is a mosaic (parent grouping node or one of its panels).</summary>
    public const string Mosaic = "mosaic";

    /// <summary>Neither exposure plans nor scanned frames — queued work, not breakage.</summary>
    public const string NoData = "no data";

    // ---- Warning: repairable authoring. ----------------------------------------------------------------
    /// <summary>The TS target has no usable coordinates: TS cannot schedule it and it can never accrue disk
    /// credit, so it is broken authoring rather than an absence of information.</summary>
    public const string NoCoords = "no-coords";

    /// <summary>Two TS targets claim the same disk unit.</summary>
    public const string Duplicate = "duplicate";

    /// <summary>The TS name and the anchored disk directory disagree.</summary>
    public const string NameMismatch = "name≠";

    /// <summary>The coordinate match had more than one candidate within tolerance.</summary>
    public const string Ambiguous = "ambiguous";

    /// <summary>2+ plans on one (filter, purpose) — routes write-back to manual.</summary>
    public const string MultiPlan = "multi-plan";

    /// <summary>A plan whose TS accepted ≠ acquired — in-session TS drift to reconcile.</summary>
    public const string AccNeAcq = "acc≠acq";

    /// <summary>True when the token marks something the user must repair. An unrecognised token reads as
    /// informative: a badge is never worth failing a load over, and the quiet tier is the safe default.</summary>
    public static bool IsWarning(string token) => token switch
    {
        NoCoords or Duplicate or NameMismatch or Ambiguous or MultiPlan or AccNeAcq => true,
        _ => false,
    };

    /// <summary>Splits a joined badge string into its tokens with each one's severity — the pure core the
    /// renderer walks, so the classification is testable without a XAML runtime. An empty string yields
    /// nothing (a clean row has no runs at all).</summary>
    public static IEnumerable<(string Token, bool IsWarning)> Split(string? badge)
    {
        if (string.IsNullOrEmpty(badge))
            yield break;
        foreach (string token in badge.Split(Separator))
            yield return (token, IsWarning(token));
    }

    /// <summary>Joins tokens into a badge string, skipping empties.</summary>
    public static string Join(IEnumerable<string> tokens) =>
        string.Join(Separator, tokens.Where(t => !string.IsNullOrEmpty(t)));
}
