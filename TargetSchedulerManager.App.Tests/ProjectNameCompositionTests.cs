using TargetSchedulerManager.App.Services;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

/// <summary>The project editor's composition bookkeeping (openspec project-name-altitude-clause): the
/// dialog edits base and altitude as separate facts; the stored name is always a composition. The pure
/// sibling owns the session state so the view wiring stays a forwarder.</summary>
public class ProjectNameCompositionTests
{
    private static Dictionary<string, object?> Seed(string name) => Seed(name, 45.0);

    private static Dictionary<string, object?> Seed(string name, object? minAlt) =>
        new(StringComparer.OrdinalIgnoreCase) { ["name"] = name, ["minimumaltitude"] = minAlt };

    [Fact]
    public void Seeds_TheBaseName_NotTheClause()
    {
        ProjectNameComposition c = ProjectNameComposition.TryCreate(Seed("Nebulae - 45"))!;
        Assert.Equal("Nebulae", c.BaseName);
        Assert.Equal("Nebulae - 45", c.StoredName);
        Assert.Equal(45, c.AltitudeDeg);
    }

    [Fact]
    public void BaseCommit_ComposesWithTheStoredAltitude()
    {
        ProjectNameComposition c = ProjectNameComposition.TryCreate(Seed("Nebulae - 45"))!;
        Assert.Equal("Nebula Survey - 45", c.ComposeForBase("Nebula Survey"));
        c.NameApplied("Nebula Survey - 45");
        Assert.Equal("Nebula Survey", c.BaseName);
    }

    [Fact]
    public void AltitudeCommit_EntailsTheRecomposedName()
    {
        ProjectNameComposition c = ProjectNameComposition.TryCreate(Seed("Nebulae - 45"))!;
        Assert.Equal("Nebulae - 40", c.AltitudeApplied(40));       // the second guarded write
        c.NameApplied("Nebulae - 40");
        Assert.Null(c.AltitudeApplied(40));                        // already composed — no rename due
    }

    [Fact]
    public void NonconformingSeed_HealsOnEitherCommit()
    {
        // Clause-less seed (inbound from TS's UI): the whole name is the base; any commit composes.
        ProjectNameComposition clauseLess = ProjectNameComposition.TryCreate(Seed("Widefield", 30.0))!;
        Assert.Equal("Widefield", clauseLess.BaseName);
        Assert.Equal("Widefield - 30", clauseLess.AltitudeApplied(30));   // same value still heals the name

        // Legacy seed: base extraction strips the retired form, so composition heals, never nests.
        ProjectNameComposition legacy = ProjectNameComposition.TryCreate(Seed("Nebulae - Above 45"))!;
        Assert.Equal("Nebulae", legacy.BaseName);
        Assert.Equal("Nebulae - 40", legacy.AltitudeApplied(40));
    }

    [Fact]
    public void ClauseLikeBase_RoundTrips()
    {
        ProjectNameComposition c = ProjectNameComposition.TryCreate(Seed("Veil - 3 - 30", 30.0))!;
        Assert.Equal("Veil - 3", c.BaseName);                      // only the final clause strips
        Assert.Equal("Veil - 3 - 25", c.AltitudeApplied(25));
    }

    [Fact]
    public void NonProjectSeed_YieldsNull_AndNullAltitudeAborts()
    {
        Assert.Null(ProjectNameComposition.TryCreate(
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["gain"] = 100L }));
        // A NULL minimumaltitude is a broken row — composing from a fabricated value would store a name
        // asserting an altitude that does not exist (rule #16: abort, never default).
        Assert.Throws<InvalidOperationException>(() => ProjectNameComposition.TryCreate(Seed("Nebulae - 45", null)));
    }

    [Fact]
    public void RawSqliteLong_SeedsTheAltitude()
    {
        // SQLite affinity can hand back INTEGER for a whole REAL — the seed is raw column values.
        ProjectNameComposition c = ProjectNameComposition.TryCreate(Seed("Nebulae - 45", 45L))!;
        Assert.Equal(45, c.AltitudeDeg);
    }
}
