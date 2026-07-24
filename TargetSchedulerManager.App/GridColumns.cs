using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TargetSchedulerManager.App;

/// <summary>
/// The reconciliation grid's ONE column ruler (openspec grid-column-ruler): the header Grid and every
/// row DataTemplate stamp their <see cref="Grid.ColumnDefinitions"/> from this table via
/// <c>local:GridColumns.ApplyRuler="True"</c>, so cells align across row kinds by construction — the
/// widths used to exist as four byte-identical XAML blocks kept in sync by hand.
/// <para>Cell <c>Grid.Column</c> indexes stay per-template (DataTemplates can't share cells): adding a
/// column starts HERE (name + width, position in the table = column index), then each template places
/// its cell — see DOMAIN.md's add-a-UI-element checklist.</para>
/// <para>The callback runs when the parser sets the attribute — before children, before layout — once
/// per Grid instance; recycled containers keep their definitions. A template missing the attribute
/// fails loudly (every cell collapses into column 0), never as subtle misalignment.</para>
/// </summary>
public static class GridColumns
{
    // Index = Grid.Column. Names match the header captions (blank-header columns keep their row meaning).
    private static readonly (string Name, GridLength Width)[] Ruler =
    [
        ("Mark", new GridLength(24)),        //  0 sync-direction arrow
        ("Enable", new GridLength(36)),      //  1 target.active checkbox
        ("Source", new GridLength(110)),     //  2 chevron + Both/TS/Disk
        ("Target", new GridLength(1, GridUnitType.Star)),   // 3 the one elastic column
        ("Project", new GridLength(170)),    //  4
        ("Filter", new GridLength(60)),      //  5
        ("Purpose", new GridLength(70)),     //  6
        ("Seconds", new GridLength(80)),     //  7
        ("Desired", new GridLength(88)),     //  8 TS goal (inline-editable on 1:1 rows)
        ("TS", new GridLength(60)),          //  9 TS's recorded acquired
        ("Actual", new GridLength(60)),      // 10 on-disk frames (ground truth)
        ("Hours", new GridLength(60)),       // 11 signed contribution pill
        ("Plans", new GridLength(45)),       // 12 ×N multiplicity
        ("Badges", new GridLength(150)),     // 13 match-state flags
    ];

    public static readonly DependencyProperty ApplyRulerProperty =
        DependencyProperty.RegisterAttached("ApplyRuler", typeof(bool), typeof(GridColumns),
            new PropertyMetadata(false, OnApplyRulerChanged));

    public static void SetApplyRuler(Grid grid, bool value) => grid.SetValue(ApplyRulerProperty, value);
    public static bool GetApplyRuler(Grid grid) => (bool)grid.GetValue(ApplyRulerProperty);

    private static void OnApplyRulerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Grid grid || e.NewValue is not true)
            return;
        grid.ColumnDefinitions.Clear();
        foreach ((_, GridLength width) in Ruler)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
    }
}
