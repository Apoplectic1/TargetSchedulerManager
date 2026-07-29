namespace TargetSchedulerManager.App.Models;

/// <summary>
/// The match-state badge vocabulary (openspec badge-severity-color): the token spellings, the separator that
/// joins them, and the two-tier severity that colours them — defined once and consumed by the loader, the
/// header rollup, and the renderer. The token strings are a soft contract: they are what the grid's search box
/// matches on (<c>ReconciliationRow.Matches</c>), so a rename changes the user's search vocabulary.
/// <para><b>Severity:</b> WARNING = authoring the user must repair **outside TSM** — by hand in NINA's TS UI
/// for the plan-side states, or on disk / in the image-management tooling for the camera-provenance ones;
/// INFORMATIVE = a fact carrying no call to action. The warning set is deliberately the same set that sets
/// <c>IsFlagged</c> and drives the flagged-only filter — colour and filter must never disagree.</para>
/// <para><b>Scope:</b> most tokens describe a whole target and therefore mark every one of its rows. The
/// <b>row-scoped</b> tokens (<see cref="IsRowScoped"/> — camera provenance and framing) describe particular
/// frames, so they mark only the rows drawing on those frames — and they display at the <b>deepest visible
/// level</b> (user rule 2026-07-29): always on the target summary row; on a collapsed rollup (the
/// triggering line is hidden inside it); on the triggering line itself once expanded, at which point the
/// rollup hands the token down instead of repeating it (<c>ReconciliationRow.BadgeText</c>). Flagging and
/// header aggregation use the full <c>Badge</c> union and are expansion-independent.</para>
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

    /// <summary>A capture directory naming no camera we recognise — repaired on disk, by renaming the
    /// directory. Row-scoped: only the rows drawing on that directory carry it.</summary>
    public const string UnknownCamera = "camera";

    /// <summary>Frames recording a camera other than the directory they sit in — filed under the wrong
    /// camera, repaired on disk. Row-scoped, like <see cref="UnknownCamera"/>.</summary>
    public const string CameraMismatch = "cam≠";

    /// <summary>A disk row whose framing (sky rotation) disagrees with the plan's — captured history that
    /// does not serve the planned framing, and a PixInsight reference-frame hazard if blindly stacked with
    /// the frames that do. Repaired outside TSM: re-frame the plan, or keep the old framing as its own
    /// composition. Row-scoped, like <see cref="UnknownCamera"/> (openspec rotation-framing-key).</summary>
    public const string Framing = "framing";

    /// <summary>True when the token marks something the user must repair. An unrecognised token reads as
    /// informative: a badge is never worth failing a load over, and the quiet tier is the safe default.</summary>
    public static bool IsWarning(string token) => token switch
    {
        NoCoords or Duplicate or NameMismatch or Ambiguous or MultiPlan or AccNeAcq
            or UnknownCamera or CameraMismatch or Framing => true,
        _ => false,
    };

    /// <summary>True when the token describes particular frames rather than a whole target — the set that
    /// follows the deepest-visible-level display rule (see the Scope paragraph above). A new frame-level
    /// token joins this list and inherits the rule; everything else displays at its own scope.</summary>
    public static bool IsRowScoped(string token) => token is UnknownCamera or CameraMismatch or Framing;

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
