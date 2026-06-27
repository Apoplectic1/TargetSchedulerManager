using System.Collections.ObjectModel;
using TargetSchedulerManager.App.Models;
using TargetSchedulerManager.App.ViewModels;
using TargetSchedulerManager.App.ViewModels.Rows;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

/// <summary>
/// The flatten/splice tree in isolation: one ExpandedContent rule drives the rebuild and every toggle's insert
/// AND remove, so the incremental splice can never drift from a full rebuild. Pure over the row objects'
/// IsExpanded flags — no view-model, no XAML.
/// </summary>
public class VisibleRowTreeTests
{
    [Fact]
    public void ExpandedContent_PlainGroup_ReturnsItsLeaves()
    {
        VisibleRowTree tree = new(new ExpansionState());
        TargetGroupRow g = Group("A", expanded: false, Make.Leaf(target: "A", filter: "H"), Make.Leaf(target: "A", filter: "O"));

        List<object> content = tree.ExpandedContent(g);

        Assert.Equal(2, content.Count);
        Assert.All(content, c => Assert.IsType<ReconciliationRow>(c));
    }

    [Fact]
    public void ExpandedContent_MosaicWithCollapsedPanels_ReturnsPanelHeadersOnly()
    {
        VisibleRowTree tree = new(new ExpansionState());
        PanelGroupRow p1 = Panel("M", "M|p1", expanded: false, Make.Leaf(target: "M"));
        PanelGroupRow p2 = Panel("M", "M|p2", expanded: false, Make.Leaf(target: "M"));
        TargetGroupRow g = Mosaic("M", expanded: true, p1, p2);

        Assert.Equal(new object[] { p1, p2 }, tree.ExpandedContent(g));
    }

    [Fact]
    public void ExpandedContent_MosaicWithExpandedPanel_IncludesThatPanelsLeaves()
    {
        VisibleRowTree tree = new(new ExpansionState());
        ReconciliationRow leaf = Make.Leaf(target: "M");
        PanelGroupRow p1 = Panel("M", "M|p1", expanded: true, leaf);
        TargetGroupRow g = Mosaic("M", expanded: true, p1);

        Assert.Equal(new object[] { p1, leaf }, tree.ExpandedContent(g));
    }

    [Fact]
    public void ExpandedContent_Rollup_ReturnsItsDetailLines()
    {
        VisibleRowTree tree = new(new ExpansionState());
        ReconciliationRow d1 = Make.Ts(target: "A", seconds: 300);
        ReconciliationRow d2 = Make.Disk(target: "A", seconds: 600);
        ReconciliationRow rollup = Make.Leaf(target: "A", mixed: true, detail: [d1, d2]);

        Assert.Equal(new object[] { d1, d2 }, tree.ExpandedContent(rollup));
    }

    [Fact]
    public void Flatten_CollapsedGroups_ReturnsHeadersOnly()
    {
        VisibleRowTree tree = new(new ExpansionState());
        TargetGroupRow a = Group("A", expanded: false, Make.Leaf(target: "A"));
        TargetGroupRow b = Group("B", expanded: false, Make.Leaf(target: "B"));

        Assert.Equal(new object[] { a, b }, tree.Flatten([a, b]));
    }

    [Fact]
    public void Flatten_ExpandedGroup_IncludesItsContent()
    {
        VisibleRowTree tree = new(new ExpansionState());
        ReconciliationRow leaf = Make.Leaf(target: "A");
        TargetGroupRow a = Group("A", expanded: true, leaf);

        Assert.Equal(new object[] { a, leaf }, tree.Flatten([a]));
    }

    [Fact]
    public void Toggle_ExpandEqualsAFullRebuild_AndCollapseRestoresExactly()
    {
        // THE invariant: the incremental splice produces the same list a wholesale rebuild would, and collapsing
        // removes exactly what expanding inserted — so the two derivations can never drift.
        VisibleRowTree tree = new(new ExpansionState());
        ReconciliationRow l1 = Make.Leaf(target: "A", filter: "H");
        ReconciliationRow l2 = Make.Leaf(target: "A", filter: "O");
        TargetGroupRow a = Group("A", expanded: false, l1, l2);
        List<TargetGroupRow> groups = [a];

        ObservableCollection<object> rows = new(tree.Flatten(groups));
        Assert.Equal(new object[] { a }, rows);

        tree.Toggle(rows, a);                               // expand
        Assert.True(a.IsExpanded);
        Assert.Equal(new object[] { a, l1, l2 }, rows);
        Assert.Equal(tree.Flatten(groups), rows.ToList());  // incremental == rebuild

        tree.Toggle(rows, a);                               // collapse
        Assert.False(a.IsExpanded);
        Assert.Equal(new object[] { a }, rows);             // exactly back to the start
    }

    [Fact]
    public void Toggle_NestedMosaic_CollapseRemovesPanelsAndTheirExpandedLeaves()
    {
        VisibleRowTree tree = new(new ExpansionState());
        ReconciliationRow leaf = Make.Leaf(target: "M");
        PanelGroupRow p = Panel("M", "M|p1", expanded: true, leaf);   // panel pre-expanded
        TargetGroupRow m = Mosaic("M", expanded: false, p);
        List<TargetGroupRow> groups = [m];

        ObservableCollection<object> rows = new(tree.Flatten(groups));   // [m]
        tree.Toggle(rows, m);                                            // expand the mosaic
        Assert.Equal(new object[] { m, p, leaf }, rows);                 // panel + its (remembered-expanded) leaf

        tree.Toggle(rows, m);                                            // collapse
        Assert.Equal(new object[] { m }, rows);                         // panel AND its leaf swept
    }

    [Fact]
    public void Toggle_PersistsExpansion_KeyedByNode()
    {
        ExpansionState exp = new();
        VisibleRowTree tree = new(exp);
        TargetGroupRow a = Group("A", expanded: false, Make.Leaf(target: "A"));
        ObservableCollection<object> rows = [a];

        tree.Toggle(rows, a);
        Assert.True(exp.IsTargetExpanded("A"));
        Assert.True(tree.IsTargetExpanded("A"));
    }

    [Fact]
    public void Toggle_Rollup_SplicesDetail_AndKeysByTargetPanelFilterPurpose()
    {
        // Pins the rollup expansion-key format the module now owns: target|panelKey|filter|purpose.
        ExpansionState exp = new();
        VisibleRowTree tree = new(exp);
        ReconciliationRow d1 = Make.Ts(target: "A", filter: "H", seconds: 300);
        ReconciliationRow d2 = Make.Disk(target: "A", filter: "H", seconds: 600);
        ReconciliationRow rollup = Make.Leaf(target: "A", filter: "H", purpose: "Light", mixed: true, detail: [d1, d2]);
        ObservableCollection<object> rows = [rollup];

        tree.Toggle(rows, rollup);
        Assert.Equal(new object[] { rollup, d1, d2 }, rows);
        Assert.True(exp.IsRollupExpanded("A||H|Light"));     // panelKey is empty for a non-panel rollup

        tree.Toggle(rows, rollup);
        Assert.Equal(new object[] { rollup }, rows);
    }

    [Fact]
    public void IsPanelExpanded_BuildsTheTargetPipePanelKey()
    {
        ExpansionState exp = new();
        exp.SetPanel("M|p1", expanded: true);
        VisibleRowTree tree = new(exp);

        Assert.True(tree.IsPanelExpanded("M", "p1"));
        Assert.False(tree.IsPanelExpanded("M", "p2"));
    }

    // ---- helpers ------------------------------------------------------------

    private static TargetGroupRow Group(string target, bool expanded, params ReconciliationRow[] leaves) =>
        new(target, leaves, expanded, isTargetEnabled: true);

    private static PanelGroupRow Panel(string target, string key, bool expanded, params ReconciliationRow[] leaves) =>
        new(target, key, key, RowSource.Both, leaves, expanded);

    private static TargetGroupRow Mosaic(string target, bool expanded, params PanelGroupRow[] panels) =>
        new(target, [.. panels.SelectMany(p => p.Children)], expanded, isTargetEnabled: true, panels);
}
