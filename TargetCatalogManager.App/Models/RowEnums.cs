namespace TargetCatalogManager.App.Models;

/// <summary>Which side(s) of the reconciliation a row's target exists on.</summary>
public enum RowSource
{
    /// <summary>On disk and in TS (resolved onto one canonical target).</summary>
    Both,

    /// <summary>In TS only — planned, nothing captured yet (or it failed to anchor to disk).</summary>
    TsOnly,

    /// <summary>On disk only — captured, but no TS plan exists.</summary>
    DiskOnly,
}

/// <summary>Which plane(s) of its cell one grid row carries.</summary>
public enum RowPlane
{
    /// <summary>A TS plan row — Desired/Acq/Acc; Hours = −(desired × seconds): the commitment shown as the
    /// deficit it contributes to its parent's total.</summary>
    Ts,

    /// <summary>A disk actuals row — the frame count; Hours = +(count × seconds) (actual integration).</summary>
    Disk,

    /// <summary>A merged plan+actuals rollup. Hours = disk − desired hours (filled caution/green); when
    /// the sub lengths aren't all one value the Seconds cell reads "mixed" (caution pill) and the row
    /// expands into its one-plane detail source lines.</summary>
    Both,
}
