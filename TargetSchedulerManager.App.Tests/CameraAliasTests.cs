using TargetSchedulerManager.App.Models;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

/// <summary>
/// The camera alias (openspec capture-config-keys): a capture directory resolves to a short alias for
/// display, and a directory naming no camera we know resolves to nothing — which is what raises the
/// <see cref="Badges.UnknownCamera"/> badge. Presentation only: the alias never enters a key, so this
/// resolution can never merge two buckets or split one.
/// <para>The alias is the identity function on every directory that exists in the live library today, so it
/// is exercised here against synthetic names — the interesting cases are precisely the ones not yet on disk.</para>
/// </summary>
public class CameraAliasTests
{
    [Theory]
    [InlineData("Z183", "Z183")]
    [InlineData("Z533", "Z533")]
    [InlineData("Q178", "Q178")]
    [InlineData("A144", "A144")]
    // The model number is what identifies the camera, so a directory naming it in another style still resolves.
    [InlineData("ASI183MM", "Z183")]
    [InlineData("ZWO ASI533MC Pro", "Z533")]
    [InlineData("QHY178", "Q178")]
    public void KnownModelNumber_Resolves(string directory, string expected) =>
        Assert.Equal(expected, Format.Camera(directory));

    [Theory]
    [InlineData("Misc")]
    [InlineData("Camera1")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void UnknownDirectory_ResolvesToNothing(string? directory) =>
        Assert.Null(Format.Camera(directory));

    [Fact]
    public void UnknownDirectory_StaysVisibleInItsCell()
    {
        // Shown raw rather than blanked, so the offending name is readable beside the badge it raises.
        Assert.Equal("Misc", Format.CameraCell("Misc"));
    }

    [Fact]
    public void KnownDirectory_ShowsItsAlias() => Assert.Equal("Z183", Format.CameraCell("ASI183MM"));

    [Fact]
    public void NoDiskSide_ShowsTheDash()
    {
        // A plan-only row has no camera at all — the em dash, per the house empty-cell convention.
        Assert.Equal(Format.Dash, Format.CameraCell(null));
        Assert.Equal(Format.Dash, Format.CameraCell("  "));
    }

    [Theory]
    [InlineData(Badges.UnknownCamera)]
    [InlineData(Badges.CameraMismatch)]
    public void CameraProvenanceTokens_AreWarnings(string token) =>
        Assert.True(Badges.IsWarning(token));
}
