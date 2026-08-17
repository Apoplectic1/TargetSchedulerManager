using Astronomy.Catalog.Scan;

namespace TargetSchedulerManager.App.Services;

/// <summary>
/// The project editor's half of the definitional altitude clause (openspec
/// <c>project-name-altitude-clause</c>): a project's stored name IS base + " - N" derived from
/// <c>minimumaltitude</c>, so the dialog edits base and altitude as separate facts and composes the
/// stored name at commit — typed text is never parsed to obtain an altitude. This is the pure sibling
/// behind the dialog's commit wiring (the untestable-surface rule): it owns the base/altitude/name
/// bookkeeping across a session's serial commits and answers what to write; the view only forwards.
/// A nonconforming seed (clause-less, legacy `Above`, disagreeing) heals on the first commit of either
/// field, because every answer is a fresh composition.
/// </summary>
internal sealed class ProjectNameComposition
{
    /// <summary>The stored (composed) project name as of the last applied write.</summary>
    public string StoredName { get; private set; }

    /// <summary>The stored altitude as of the last applied write.</summary>
    public double AltitudeDeg { get; private set; }

    /// <summary>The base name the dialog's name field shows — extraction, never storage.</summary>
    public string BaseName => MosaicConvention.ExtractBaseName(StoredName);

    private ProjectNameComposition(string storedName, double altitudeDeg)
    {
        StoredName = storedName;
        AltitudeDeg = altitudeDeg;
    }

    /// <summary>
    /// Builds the session from a project row's seed, or null when the seed carries no name (non-project
    /// callers). A NULL <c>minimumaltitude</c> aborts — TS declares the column non-nullable, and
    /// composing from a fabricated value would store a name asserting an altitude that does not exist.
    /// </summary>
    public static ProjectNameComposition? TryCreate(IReadOnlyDictionary<string, object?> seed)
    {
        if (!seed.TryGetValue("name", out object? name) || name is not string storedName)
            return null;
        double altitude = seed.TryGetValue("minimumaltitude", out object? raw) && raw is not null
            ? Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture)
            : throw new InvalidOperationException(
                $"project \"{storedName}\" has NULL minimumaltitude — TS declares that column non-nullable");
        return new ProjectNameComposition(storedName, altitude);
    }

    /// <summary>The stored-name value a base-name commit writes: the new base composed with the stored
    /// altitude.</summary>
    public string ComposeForBase(string newBaseName) =>
        MosaicConvention.ComposeAltitudeName(newBaseName, AltitudeDeg);

    /// <summary>Records an applied name write.</summary>
    public void NameApplied(string composedName) => StoredName = composedName;

    /// <summary>
    /// Records an applied <c>minimumaltitude</c> write and answers the recomposed name it entails —
    /// null when the stored name already matches (no second write due). The rename is a SECOND guarded
    /// write (two push-review lines, the Set-press precedent).
    /// </summary>
    public string? AltitudeApplied(double newAltitudeDeg)
    {
        AltitudeDeg = newAltitudeDeg;
        string composed = MosaicConvention.ComposeAltitudeName(BaseName, newAltitudeDeg);
        return composed == StoredName ? null : composed;
    }
}
