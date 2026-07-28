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
        // The capture configuration (openspec capture-config-keys). Gain/Offset/Bin are reconciliation keys —
        // rows separate on them — so they must be legible on the row that separated. Camera is disk-side only
        // (a TS plan cannot name one) and never separates a row; it is carried for the second purpose, showing
        // the imaging history. NOTE: these four are deliberately EXCLUDED from sort precedence even though they
        // sit left of Filter — see ReconciliationLoader's sort, which keeps one filter's rows contiguous.
        ("Camera", new GridLength(60)),      //  5
        ("Gain", new GridLength(50)),        //  6
        ("Offset", new GridLength(50)),      //  7
        ("Bin", new GridLength(40)),         //  8
        ("Filter", new GridLength(60)),      //  9
        ("Purpose", new GridLength(70)),     // 10
        ("Seconds", new GridLength(80)),     // 11
        ("Desired", new GridLength(88)),     // 12 TS goal (inline-editable on 1:1 rows)
        ("TS", new GridLength(60)),          // 13 TS's recorded acquired
        ("Actual", new GridLength(60)),      // 14 on-disk frames (ground truth)
        ("Hours", new GridLength(60)),       // 15 signed contribution pill
        ("Plans", new GridLength(45)),       // 16 ×N multiplicity
        ("Badges", new GridLength(150)),     // 17 match-state flags
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
