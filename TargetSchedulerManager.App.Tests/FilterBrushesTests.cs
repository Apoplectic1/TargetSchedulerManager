using TargetSchedulerManager.App.Models;

using Windows.UI;

using Xunit;

namespace TargetSchedulerManager.App.Tests;

/// <summary>
/// The filter-identity palette's pure core (openspec filter-colored-rows): the code→color map. The
/// brush layer wraps WinUI objects — no XAML runtime here, so the contract worth pinning is the color
/// selection: the six palette hues at the wash alpha, and plain (null) for L and everything unknown.
/// </summary>
public class FilterBrushesTests
{
    [Theory]
    [InlineData("O", 0, 210, 255)]   // cyan — split from G's green (2026-08-05 contrast tune)
    [InlineData("H", 255, 0, 15)]    // the pure-red anchor
    [InlineData("S", 255, 0, 128)]   // crimson — split from H
    [InlineData("B", 0, 69, 255)]
    [InlineData("G", 0, 255, 61)]
    [InlineData("R", 255, 120, 0)]   // orange — split from H
    public void PaletteCode_MapsToItsPassbandHue_AtWashAlpha(string filter, byte r, byte g, byte b)
    {
        Color? color = FilterBrushes.WashColor(filter);

        Assert.Equal(Color.FromArgb(FilterBrushes.WashAlpha, r, g, b), color);
    }

    [Theory]
    [InlineData("L")]                        // deliberately plain — user call, not a gap
    [InlineData("")]
    [InlineData("Ha")]                       // exact match only: the code the Filter column renders
    [InlineData("o")]                        // no case folding — off-palette is plain, never guessed
    [InlineData("2024-10-18 - Track Comet")] // a directory-shaped stray reads plain, no warning
    public void OffPalette_IsPlain_NoFallbackHue(string filter) =>
        Assert.Null(FilterBrushes.WashColor(filter));
}
