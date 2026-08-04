using TargetSchedulerManager.App.Models;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

/// <summary>
/// The badge vocabulary's pure core (openspec badge-severity-color): the two-tier severity and the
/// split/join round trip. The rendering itself is an attached property over <c>TextBlock.Inlines</c> — no XAML
/// runtime here, so everything worth pinning lives in these pure functions.
/// </summary>
public class BadgesTests
{
    [Theory]
    [InlineData(Badges.Duplicate)]
    [InlineData(Badges.NameMismatch)]
    [InlineData(Badges.Ambiguous)]
    [InlineData(Badges.MultiPlan)]
    [InlineData(Badges.AccNeAcq)]
    [InlineData(Badges.NoCoords)]     // repairable authoring: TS cannot schedule a coordinate-less target
    [InlineData(Badges.NoRotation)]   // rotation is a required TS-target parameter
    [InlineData(Badges.Sentinel)]     // a camera-default template value: repaired in the template, by hand
    public void WarningTier_IsTheRepairableAuthoringSet(string token) =>
        Assert.True(Badges.IsWarning(token));

    [Fact]
    public void NoRotation_IsTargetScope_NotRowScoped() =>
        Assert.False(Badges.IsRowScoped(Badges.NoRotation));

    [Fact]
    public void Sentinel_IsRowScoped() =>
        Assert.True(Badges.IsRowScoped(Badges.Sentinel));

    [Theory]
    [InlineData(Badges.Mosaic)]
    [InlineData(Badges.NoData)]       // queued work, not breakage
    public void InformativeTier_CarriesNoCallToAction(string token) =>
        Assert.False(Badges.IsWarning(token));

    [Fact]
    public void UnknownToken_ReadsAsInformative_NeverThrows() =>
        Assert.False(Badges.IsWarning("something-new"));

    [Fact]
    public void Split_ResolvesSeverityPerToken_SoAMixedCellShowsBoth()
    {
        (string Token, bool IsWarning)[] parts =
            Badges.Split($"{Badges.Mosaic}{Badges.Separator}{Badges.MultiPlan}").ToArray();

        Assert.Equal(2, parts.Length);
        Assert.Equal((Badges.Mosaic, false), parts[0]);
        Assert.Equal((Badges.MultiPlan, true), parts[1]);
    }

    [Fact]
    public void Split_OnEmptyOrNull_YieldsNothing()
    {
        Assert.Empty(Badges.Split(""));
        Assert.Empty(Badges.Split(null));
    }

    [Fact]
    public void SplitJoin_RoundTrips()
    {
        string badge = Badges.Join([Badges.Mosaic, Badges.NameMismatch, Badges.AccNeAcq]);

        Assert.Equal(badge, Badges.Join(Badges.Split(badge).Select(p => p.Token)));
    }

    [Fact]
    public void Join_SkipsEmptyTokens() =>
        Assert.Equal(Badges.Mosaic, Badges.Join(["", Badges.Mosaic, ""]));

    /// <summary>A token whose spelling contains the separator's spacing would split itself apart — the
    /// vocabulary must stay separator-free ("no data" uses a plain space, not " · ").</summary>
    [Fact]
    public void NoTokenContainsTheSeparator()
    {
        string[] all =
        [
            Badges.Mosaic, Badges.NoData, Badges.NoCoords, Badges.NoRotation, Badges.Duplicate,
            Badges.NameMismatch, Badges.Ambiguous, Badges.MultiPlan, Badges.AccNeAcq,
            Badges.Sentinel,
        ];

        Assert.All(all, t => Assert.DoesNotContain(Badges.Separator, t));
    }
}
