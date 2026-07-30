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
    /// <summary>The shared gutter every data column adds over its content maximum (user obs 27ec,
    /// 2026-07-29: "column spacing between Camera and Badges should be equal and uniform"). Data cells are
    /// CENTERED, so the perceived gap between neighbours is each column's slack — making the slack a single
    /// constant makes the gaps uniform by construction. Change spacing here, not per column.</summary>
    private const int Gutter = 24;

    // Index = Grid.Column. Names match the header captions (blank-header columns keep their row meaning).
    // Data-column widths are content-max + Gutter; the content number cited is the widest thing the column
    // ever shows (a pill with its padding, the header caption, or the widest real value).
    private static readonly (string Name, GridLength Width)[] Ruler =
    [
        ("Mark", new GridLength(24)),        //  0 sync-direction arrow
        ("Enable", new GridLength(36)),      //  1 target.active checkbox
        ("Source", new GridLength(110)),     //  2 chevron + Both/TS/Disk
        ("Target", new GridLength(1, GridUnitType.Star)),   // 3 the one elastic column — the WINDOW width is
                                             //    chosen so this never truncates a real target name
                                             //    (user obs 27ec); longest today: "Mosaic - Cygnus Loop · 16 panels"
        ("Project", new GridLength(170)),    //  4
        // The capture configuration (openspec capture-config-keys + rotation-framing-key). Gain/Offset/Bin/
        // Rot are reconciliation keys — rows separate on them — so they must be legible on the row that
        // separated. Camera is disk-side only (a TS plan cannot name one) and never separates a row; it is
        // carried for the second purpose, showing the imaging history. Rot shows the framing's fold-180
        // rotation (sky plain, mechanical marked "°(M)", dash when unexpressed). NOTE: these five are
        // deliberately EXCLUDED from sort precedence even though they sit left of Filter — see
        // ReconciliationLoader's sort, which keeps one filter's rows contiguous.
        ("Camera", new GridLength(46 + Gutter)),   //  5 "mixed" pill / header "Camera"
        ("Gain", new GridLength(56 + Gutter)),     //  6 the sentinel pill "default"
        ("Offset", new GridLength(56 + Gutter)),   //  7 the sentinel pill "default"
        ("Bin", new GridLength(30 + Gutter)),      //  8 "1x2"
        ("Rot", new GridLength(62 + Gutter)),      //  9 "110.5°(M)" (user obs 53c5)
        ("Filter", new GridLength(32 + Gutter)),   // 10 header "Filter"; letters are 1–3 chars
        ("Purpose", new GridLength(46 + Gutter)),  // 11 header "Purpose"
        ("Seconds", new GridLength(48 + Gutter)),  // 12 "mixed" pill / header "Seconds"
        ("Desired", new GridLength(44 + Gutter)),  // 13 TS goal; the 40px inline NumberBox
        ("TS", new GridLength(30 + Gutter)),       // 14 4-digit acquired
        ("Actual", new GridLength(34 + Gutter)),   // 15 header "Actual" / 4-digit frames
        ("Hours", new GridLength(52 + Gutter)),    // 16 "-291.6" pill
        ("Plans", new GridLength(30 + Gutter)),    // 17 "×16"
        ("Badges", new GridLength(150)),           // 18 match-state flags (left-aligned; own 10px margin)
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
