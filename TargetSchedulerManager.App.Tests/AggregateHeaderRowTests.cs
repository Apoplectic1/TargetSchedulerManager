using TargetSchedulerManager.App.Models;
using TargetSchedulerManager.App.ViewModels.Rows;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

/// <summary>
/// TargetGroupRow and PanelGroupRow now share <see cref="AggregateHeaderRow"/> — so for the same children they
/// must render identical aggregate display (the dedup's whole point: one implementation, no drift between the
/// two header levels). The Brush getters stay app-verified (no XAML runtime here).
/// </summary>
public class AggregateHeaderRowTests
{
    private static ReconciliationRow[] Children() =>
    [
        Make.Leaf(target: "A", filter: "H", desired: 10, disk: 6, planHours: 0.83, diskHours: 0.5),
        Make.Disk(target: "A", filter: "O", frames: 4),
        Make.Leaf(target: "A", filter: "S", flagged: true, badge: "name≠", desired: 5, disk: 5),
    ];

    [Fact]
    public void TargetAndPanelHeaders_RenderIdenticalAggregates_ForTheSameChildren()
    {
        ReconciliationRow[] kids = Children();
        AggregateHeaderRow target = new TargetGroupRow("A", kids, isExpanded: false, isTargetEnabled: true);
        AggregateHeaderRow panel = new PanelGroupRow("A", "A|p1", "Panel 01", RowSource.Both, kids, isExpanded: false);

        Assert.Equal(target.DesiredText, panel.DesiredText);
        Assert.Equal(target.AcquiredText, panel.AcquiredText);
        Assert.Equal(target.AcceptedText, panel.AcceptedText);
        Assert.Equal(target.DiskText, panel.DiskText);
        Assert.Equal(target.HoursText, panel.HoursText);
        Assert.Equal(target.SourceText, panel.SourceText);
        Assert.Equal(target.Badge, panel.Badge);
        Assert.Equal(target.Remaining, panel.Remaining);
        Assert.Equal(target.Delta, panel.Delta);
        Assert.Equal(target.IsFlagged, panel.IsFlagged);
        Assert.NotEqual("—", target.HoursText);   // and the shared hours actually rendered a value
    }

    [Fact]
    public void IsExpanded_FlipsTheChevron_AndRaisesINPC()
    {
        AggregateHeaderRow header =
            new TargetGroupRow("A", [Make.Leaf(target: "A")], isExpanded: false, isTargetEnabled: true);
        List<string> raised = [];
        header.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        string collapsed = header.ChevronGlyph;
        header.IsExpanded = true;
        Assert.NotEqual(collapsed, header.ChevronGlyph);   // chevron glyph flipped on expand
        Assert.Contains(nameof(AggregateHeaderRow.IsExpanded), raised);
        Assert.Contains(nameof(AggregateHeaderRow.ChevronGlyph), raised);
    }

    [Fact]
    public void Recompute_AfterAnInPlaceEdit_RefreshesTheTotals()
    {
        ReconciliationRow leaf = Make.Leaf(target: "A", desired: 10, planSeconds: 300, disk: 0);
        AggregateHeaderRow header = new TargetGroupRow("A", [leaf], isExpanded: false, isTargetEnabled: true);
        Assert.Equal("10", header.DesiredText);

        leaf.ApplyDesired(25);
        header.Recompute();
        Assert.Equal("25", header.DesiredText);
    }
}
