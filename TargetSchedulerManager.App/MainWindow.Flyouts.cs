using Astronomy.Catalog.TargetScheduler;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TargetSchedulerManager.App.Controls;
using TargetSchedulerManager.App.Models;
using TargetSchedulerManager.App.Services;
using TargetSchedulerManager.App.ViewModels;
using TargetSchedulerManager.App.ViewModels.Rows;
using static TargetSchedulerManager.App.Shared.UiTask;   // FireAndLog — the fire-and-forget seam (review N3)

namespace TargetSchedulerManager.App;

// Context-sensitive field editing (presentation split P2): the edit-glyph/right-click triggers, the
// row context menu, the Templates… picker, the mosaic-project flyout, and the schema-driven edit flyout
// with its commit routing. The handlers/toolbar plumbing live in the core part; the sync dialogs in
// MainWindow.Dialogs.cs.
public sealed partial class MainWindow
{
    // The hover-revealed edit glyph: the row templates name their glyph button "EditGlyph" (Opacity 0 at rest,
    // Visibility gated by the row's TS key), and the template-root Grid flips its opacity on pointer over.
    private void Row_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement root && root.FindName("EditGlyph") is UIElement glyph)
            glyph.Opacity = 1;
    }

    private void Row_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement root && root.FindName("EditGlyph") is UIElement glyph)
            glyph.Opacity = 0;
    }

    private void EditTarget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.DataContext is not TargetGroupRow group)
            return;
        if (group.IsMosaic)
            FireAndLog(() => ShowMosaicFlyoutAsync(el, group), "mosaic flyout");
        else if (group.TsTargetKey is string key)
            FireAndLog(() => ShowEditFlyoutAsync(el, TsTable.Target, key, group.Target, group, null), "target flyout");
    }

    // A mosaic panel is a normal TS target — the standard target flyout, keyed by the panel's own row.
    private void EditPanelTarget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is PanelGroupRow { TsTargetKey: string key } panel)
            FireAndLog(() => ShowEditFlyoutAsync(el, TsTable.Target, key, Format.Label(panel.Target, panel.Label), null, null), "panel flyout");
    }

    private void EditPlan_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is ReconciliationRow { PlanTsKey: string key } row)
            FireAndLog(() => ShowEditFlyoutAsync(el, TsTable.ExposurePlan, key, Format.Label(row.Target, row.Filter), null, row), "plan flyout");
    }

    // Right-click anywhere on a TS-backed row: a context menu whose items are gated by the row's data — the
    // extension point for future per-row actions (template editing, cadence actions). Handled centrally on the
    // ListView so the templates stay menu-free. Items are additive: a row offers its own editor plus
    // "Edit project…" whenever it resolves a TS project key (the project has no rows of its own — any of its
    // rows anchors the same project flyout). The mosaic parent's dedicated flyout stays its project entry.
    private void Row_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement el)
            return;

        MenuFlyout menu = new();
        string? projectKey = null, projectName = null;
        switch (el.DataContext)
        {
            case TargetGroupRow { IsMosaic: true, ProjectTsKey: not null } mosaic:
                menu.Items.Add(EditMenuItem("Edit mosaic project…", () => ShowMosaicFlyoutAsync(el, mosaic)));
                break;
            case TargetGroupRow group:
                if (group is { CanEnable: true, TsTargetKey: string targetKey })
                    menu.Items.Add(EditMenuItem("Edit target…",
                        () => ShowEditFlyoutAsync(el, TsTable.Target, targetKey, group.Target, group, null)));
                (projectKey, projectName) = (group.ProjectTsKey, group.Project);
                break;
            case PanelGroupRow panel:
                if (panel.TsTargetKey is string panelKey)
                    menu.Items.Add(EditMenuItem("Edit panel target…",
                        () => ShowEditFlyoutAsync(el, TsTable.Target, panelKey, Format.Label(panel.Target, panel.Label), null, null)));
                (projectKey, projectName) = (panel.Children[0].ProjectTsKey, panel.Children[0].Project);
                break;
            case ReconciliationRow row:
                if (row.PlanTsKey is string planKey)
                {
                    menu.Items.Add(EditMenuItem("Edit exposure plan…",
                        () => ShowEditFlyoutAsync(el, TsTable.ExposurePlan, planKey, Format.Label(row.Target, row.Filter), null, row)));
                    // The template BEHIND this plan — shared config, so the flyout title carries the blast radius.
                    if (ViewModel.TryGetTemplateForPlan(planKey) is { } template)
                        menu.Items.Add(EditMenuItem("Edit template…",
                            () => ShowEditFlyoutAsync(el, TsTable.ExposureTemplate, template.TsKey, TemplateTitle(template), null, null)));
                }
                (projectKey, projectName) = (row.ProjectTsKey, row.Project);
                break;
        }
        if (projectKey is string prjKey)
            menu.Items.Add(EditMenuItem("Edit project…",
                () => ShowEditFlyoutAsync(el, TsTable.Project, prjKey, $"{projectName} — project", null, null)));

        if (menu.Items.Count == 0)
            return;   // disk-only rows, rollups: no menu

        menu.ShowAt(el, new FlyoutShowOptions { Position = e.GetPosition(el) });
        e.Handled = true;
    }

    // The mosaic-project flyout — the mosaic special case (user decision 2026-07-06): a mosaic parent is a
    // grouping node with no TS target row, so its flyout edits the two whole-mosaic knobs instead: a master
    // enable (fan-out target.active to every panel; tri-state display when panels disagree) and the TS
    // project's priority (TS-native cascade — every panel left at Default (-1) inherits it in scoring;
    // per-panel overrides survive). Panels themselves keep the standard target flyout.
    private async Task ShowMosaicFlyoutAsync(FrameworkElement anchor, TargetGroupRow group)
    {
        if (group.ProjectTsKey is not string projectKey)
            return;

        IReadOnlyDictionary<string, object?>? seed =
            await ViewModel.ReadTsFieldsAsync(TsTable.Project, projectKey, group.Target);

        StackPanel form = new() { Spacing = 8, MinWidth = 280, Padding = new Thickness(4) };
        form.Children.Add(new TextBlock
        {
            Text = $"{group.TargetText} — mosaic project",
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            Margin = new Thickness(0, 0, 0, 4),
        });

        // Per-field marks, hand-wired (this flyout isn't schema-generated): the master enable's mark is the
        // union over the panels' target.active states (tooltip lists per-panel lines — a fan-out control
        // carries a fan-in mark), priority resolves the project field. Refreshed after every commit here,
        // same as the generated forms.
        TextBlock enableMark = MosaicMark();
        TextBlock priorityMark = MosaicMark();
        void RefreshMosaicMarks()
        {
            SyncMarks marks = ViewModel.BuildMarks();
            bool anyIn = false, anyOut = false;
            List<string> lines = [];
            foreach (PanelGroupRow panel in group.Panels ?? [])
            {
                if (panel.TsTargetKey is not string panelKey)
                    continue;
                (string glyph, string? tooltip) = marks.ForField(TsTable.Target, panelKey, "active");
                if (glyph.Length == 0)
                    continue;
                anyIn |= glyph is SyncMarks.In or SyncMarks.BothWays;
                anyOut |= glyph is SyncMarks.Out or SyncMarks.BothWays;
                if (tooltip is not null)
                    lines.AddRange(tooltip.Split('\n').Select(line => $"{panel.Label}: {line}"));
            }
            ApplyMark(enableMark,
                anyIn && anyOut ? SyncMarks.BothWays : anyIn ? SyncMarks.In : anyOut ? SyncMarks.Out : "",
                lines.Count == 0 ? null : string.Join("\n", lines));
            (string projectGlyph, string? projectTip) = marks.ForField(TsTable.Project, projectKey, "priority");
            ApplyMark(priorityMark, projectGlyph, projectTip);
        }

        // Master enable: checked = all TS-backed panels active, indeterminate = mixed. Click always yields a
        // definite on/off (user gesture is a master switch); a partial failure re-reads whatever state resulted.
        CheckBox enableAll = new()
        {
            Content = "Enable all panels",
            IsChecked = ViewModel.GetMosaicEnabledState(group),
            IsThreeState = false,
            MinWidth = 0,
        };
        ToolTipService.SetToolTip(enableAll, "Writes target.active on every panel target");
        enableAll.Click += async (_, _) =>
        {
            bool wanted = enableAll.IsChecked == true;
            if (!await ViewModel.SetMosaicEnabledAsync(group, wanted))
                enableAll.IsChecked = ViewModel.GetMosaicEnabledState(group);
            RefreshMosaicMarks();
        };
        form.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 12, Children = { enableMark, enableAll },
        });

        // Project priority (TS ProjectPriority): seeded from the project row; commit on selection.
        if (seed is not null && seed.TryGetValue("priority", out object? rawPriority))
        {
            IReadOnlyList<TsEnumValue> values = TsEditableSchema.EnumValues("ProjectPriority");
            long committed = System.Convert.ToInt64(rawPriority ?? 0L, System.Globalization.CultureInfo.InvariantCulture);
            ComboBox combo = new()
            {
                ItemsSource = values,
                DisplayMemberPath = nameof(TsEnumValue.Label),
                SelectedItem = values.FirstOrDefault(v => v.Code == committed),
                MinWidth = 120,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(combo, "Panels with priority Default (−1) inherit this in TS scoring");
            combo.SelectionChanged += async (_, _) =>
            {
                if (combo.SelectedItem is not TsEnumValue picked || picked.Code == committed) return;
                if (await ViewModel.SetTsFieldAsync(TsTable.Project, projectKey, "priority", picked.Code, group.Target))
                    committed = picked.Code;
                else
                    combo.SelectedItem = values.FirstOrDefault(v => v.Code == committed);
                RefreshMosaicMarks();
            };

            StackPanel priorityRow = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
            priorityRow.Children.Add(priorityMark);
            priorityRow.Children.Add(new TextBlock { Text = "Project priority", VerticalAlignment = VerticalAlignment.Center });
            priorityRow.Children.Add(combo);
            form.Children.Add(priorityRow);
        }
        else
        {
            form.Children.Add(new TextBlock
            {
                Text = "Couldn't read the TS project — see tsm.log.",
                Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 280,
            });
        }

        RefreshMosaicMarks();
        Flyout flyout = new() { Content = form, Placement = FlyoutPlacementMode.Bottom };
        flyout.ShowAt(anchor);
    }

    private static TextBlock MosaicMark() => new()
    {
        MinWidth = 18,
        TextAlignment = TextAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static void ApplyMark(TextBlock block, string glyph, string? tooltip)
    {
        block.Text = glyph;
        ToolTipService.SetToolTip(block, tooltip);
    }

    private static MenuFlyoutItem EditMenuItem(string text, Func<Task> open)
    {
        MenuFlyoutItem item = new() { Text = text, Icon = new FontIcon { Glyph = "" } };
        item.Click += (_, _) => FireAndLog(open, "row menu action");
        return item;
    }

    // The Templates… picker: every template from the loaded graph (zero-use ones included — they have no
    // rows to anchor from), each opening the standard schema-generated flyout with the shared-scope title.
    private void Templates_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<TemplateInfo> templates = ViewModel.ListTemplates();
        if (templates.Count == 0)
        {
            ViewModel.NoteStatus("no templates to edit — they come from the loaded TS read (load first)");
            return;
        }
        MenuFlyout menu = new();
        SyncMarks marks = ViewModel.BuildMarks();
        foreach (TemplateInfo template in templates)
        {
            // The template's own sync-direction mark (grid column-0 language) + old→new tooltip when pending.
            (string glyph, string? tooltip) = marks.ForTemplate(template.TsKey);
            string prefix = glyph.Length > 0 ? $"{glyph} " : "";
            MenuFlyoutItem item = EditMenuItem($"{prefix}{template.Name} · {template.Filter} — used by {template.UsedByPlans} plan(s)",
                () => ShowEditFlyoutAsync((FrameworkElement)sender, TsTable.ExposureTemplate, template.TsKey,
                    TemplateTitle(template), null, null));
            if (tooltip is not null)
                ToolTipService.SetToolTip(item, tooltip);
            menu.Items.Add(item);
        }
        menu.ShowAt((FrameworkElement)sender);
    }

    private static string TemplateTitle(TemplateInfo template) =>
        $"Template '{template.Name}' — used by {template.UsedByPlans} plan(s)";

    // The per-field mark resolver behind a flyout's leading column: one fresh SyncMarks per refresh pass
    // (the editor batches all columns into one call), ForField per column. Fresh facts every pass — the
    // whole point is the mark flipping → the moment a commit lands.
    private TsFieldsEditor.MarkResolver MarkResolverFor(TsTable table, string key) => columns =>
    {
        SyncMarks marks = ViewModel.BuildMarks();
        return columns.ToDictionary(
            column => column, column => marks.ForField(table, key, column), StringComparer.OrdinalIgnoreCase);
    };

    // Seeds the schema-driven form from the current db (off the UI thread), then shows it in a flyout anchored
    // at the gesture's row. Every field commits itself through the guarded gate; fields with dedicated in-grid
    // controls route through their specific setters so their cells refresh in place. Per-field commit means
    // light-dismiss can never lose work — no confirmation needed on close.
    private async Task ShowEditFlyoutAsync(
        FrameworkElement anchor, TsTable table, string key, string title,
        TargetGroupRow? group, ReconciliationRow? row)
    {
        IReadOnlyDictionary<string, object?>? seed = await ViewModel.ReadTsFieldsAsync(table, key, title);

        // Live resolver behind the exposure sentinel: the row's effective seconds. The commit path mirrors the
        // row before the control re-consults this (SetPlanExposureAsync resolves the template default via the
        // plan→template join), so after a revert-to-default the box/label show the real default immediately —
        // and the control only treats it as "the default" while the column actually holds the sentinel.
        TsFieldsEditor.EffectiveValue? effective = row is null ? null :
            column => string.Equals(column, "exposure", StringComparison.OrdinalIgnoreCase) && row.PlanSeconds > 0
                ? row.PlanSeconds
                : null;

        // Project pair-warn (warn-never-block): TS's own Save refuses when min time > 2 × meridian window —
        // per-field commit can't block without forcing an edit order, so the pair is re-evaluated from the
        // seed + each verified commit and surfaced as a persistent caution that clears when the pair is fixed.
        Dictionary<string, object?> current = seed is null
            ? new(StringComparer.OrdinalIgnoreCase)
            : new(seed, StringComparer.OrdinalIgnoreCase);
        TextBlock? pairWarn = table != TsTable.Project ? null : new TextBlock
        {
            Text = "Min time > 2 × Meridian window — TS will never select this project",
            Foreground = ThemeBrushes.CautionText,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 300,
        };

        UIElement content = TsFieldsEditor.Create(table, title, seed, async (column, value) =>
        {
            if (TryCommitMirroredField(group, row, column, value) is { } mirrored)
                return await mirrored;
            bool applied = await ViewModel.SetTsFieldAsync(table, key, column, value, title);
            if (applied && pairWarn is not null)
            {
                current[column] = value;
                RefreshPairWarn();
            }
            return applied;
        }, effective, marks: MarkResolverFor(table, key));

        if (pairWarn is not null)
        {
            RefreshPairWarn();   // an already-invalid pair warns from the moment the flyout opens
            content = new StackPanel { Spacing = 6, Children = { content, pairWarn } };
        }

        Flyout flyout = new() { Content = content, Placement = FlyoutPlacementMode.Bottom };
        flyout.ShowAt(anchor);

        void RefreshPairWarn()
        {
            bool never = Shared.ProjectRules.IsNeverSelected(
                current.GetValueOrDefault("minimumtime"), current.GetValueOrDefault("meridianwindow"));
            pairWarn!.Visibility = never ? Visibility.Visible : Visibility.Collapsed;
            if (never)
                ViewModel.NoteStatus($"{title}: Min time > 2 × Meridian window — TS will never select this project");
        }
    }

    // The in-grid-mirror routing table (review M7 — was an inline lambda of stringly special cases):
    // these columns have dedicated setters that refresh their grid cells in place; null = not a mirrored
    // column, the caller falls through to the generic SetTsFieldAsync path (whose pair-warn bookkeeping
    // stays with its captures in the flyout lambda). Row-context and group-context columns are disjoint —
    // a flyout opens with exactly one of the two.
    private Task<bool>? TryCommitMirroredField(
        TargetGroupRow? group, ReconciliationRow? row, string column, object? value) =>
        (row, group, column.ToLowerInvariant()) switch
        {
            ({ } r, _, "enabled") => ViewModel.SetPlanEnabledAsync(r, System.Convert.ToInt64(value) != 0),
            (_, { } g, "active") => ViewModel.SetTargetEnabledAsync(g, System.Convert.ToInt64(value) != 0),
            ({ } r, _, "desired") => ViewModel.SetPlanDesiredAsync(r, System.Convert.ToInt32(value)),
            ({ } r, _, "exposure") => CommitExposureAsync(r, value),
            _ => null,
        };

    // Seconds-cell mirror: the rounded override (0 is a literal zero-second exposure); only the
    // negative defer-to-template sentinel (null) resolves via the db.
    private Task<bool> CommitExposureAsync(ReconciliationRow row, object? value)
    {
        double v = System.Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
        return ViewModel.SetPlanExposureAsync(row, v, v >= 0 ? (int)System.Math.Round(v) : null);
    }
}
