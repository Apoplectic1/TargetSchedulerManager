using Astronomy.Catalog.TargetScheduler;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TargetSchedulerManager.App.Controls;
using TargetSchedulerManager.App.Shared;
using TargetSchedulerManager.App.ViewModels;
using TargetSchedulerManager.App.ViewModels.Rows;

namespace TargetSchedulerManager.App;

/// <summary>
/// Thin code-behind, WinForms-style: event handlers forward control state to the view-model, which owns all
/// logic. Display updates flow back through {x:Bind}/INotifyPropertyChanged rather than direct control writes.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; } = new();

    private bool _initialLoadStarted;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Target Scheduler Manager — local TS copy · push to BIRDWATCHER";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1560, 980));

        // The sync dialogs (open-with-dirty, push review) are ContentDialogs and need a live XamlRoot, which
        // only exists once the window content has loaded — so the initial load waits for Loaded instead of
        // firing in the constructor.
        ViewModel.OpenWithDirtyPrompt = ShowOpenDirtyDialogAsync;
        ViewModel.ConfirmPushPrompt = ShowPushReviewDialogAsync;
        ((FrameworkElement)Content).Loaded += (_, _) =>
        {
            if (_initialLoadStarted) return;
            _initialLoadStarted = true;
            _ = ViewModel.LoadAsync(PullPolicy.IfChanged);   // VM surfaces progress/errors via IsLoading/StatusText
        };
    }

    // Reload = rescan the disk + re-read the local db; it never pulls (pull is an open/Pull-now decision).
    private void Reload_Click(object sender, RoutedEventArgs e) => _ = ViewModel.LoadAsync(PullPolicy.Never);

    private void Push_Click(object sender, RoutedEventArgs e) => _ = ViewModel.PushAsync();

    private void PullNow_Click(object sender, RoutedEventArgs e) => _ = ViewModel.LoadAsync(PullPolicy.Force);

    private void CancelPull_Click(object sender, RoutedEventArgs e) => ViewModel.CancelPull();

    // Write + open the printable ambiguity report (fixes happen by hand in NINA's TS UI, never here).
    private void Ambiguities_Click(object sender, RoutedEventArgs e) => ViewModel.WriteAmbiguityReport();

    // One press, no confirm: enables/disables target.active by tonight's visibility, projects follow.
    private void VisibleTonight_Click(object sender, RoutedEventArgs e) => _ = ViewModel.RunVisibleTonightAsync();

    private void Search_TextChanged(object sender, TextChangedEventArgs e) =>
        ViewModel.SearchText = SearchBox.Text;

    private void SourceFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.SourceFilterIndex = SourceFilter.SelectedIndex;

    private void FlaggedOnly_Changed(object sender, RoutedEventArgs e) =>
        ViewModel.FlaggedOnly = FlaggedOnly.IsChecked == true;

    private void Sort_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.SortMode = (SortMode)SortPicker.SelectedIndex;

    // Whole-row click toggles a disclosure: target group headers, mosaic panels, and mixed-seconds
    // rollups; clicks on plain one-plane rows fall through.
    // Whole-row click toggles a disclosure: target group headers, mosaic panels, and mixed-seconds rollups;
    // clicks on plain one-plane rows fall through.
    private void Row_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ViewModels.Rows.TargetGroupRow group)
            ViewModel.ToggleGroup(group);
        else if (e.ClickedItem is ViewModels.Rows.PanelGroupRow panel)
            ViewModel.TogglePanel(panel);
        else if (e.ClickedItem is ViewModels.Rows.ReconciliationRow { Detail: not null } rollup)
            ViewModel.ToggleRollup(rollup);
    }

    // The leftmost enable checkbox on a target header: write target.active to the TS copy immediately. Click
    // fires only on user interaction (not on the IsChecked binding), so there's no spurious write at bind time;
    // on a failed/unverified write we put the box back. The checkbox swallows the click, so Row_ItemClick
    // (whole-row expand) does not also fire.
    private async void TargetEnable_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox box || box.DataContext is not ViewModels.Rows.TargetGroupRow group)
            return;
        bool now = box.IsChecked == true;
        if (!await ViewModel.SetTargetEnabledAsync(group, now))
            box.IsChecked = !now;   // write failed — restore the prior state
    }

    // Per-plan enable: writes directly, no confirmation (user decision 2026-07-07) — the library clears the
    // target's cadence rows in the same transaction, TS regenerates the sequence from the new plan set on its
    // next pass (slot-0 restart accepted), and nothing reaches BIRDWATCHER until the reviewed push.
    private async void PlanEnable_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox box || box.DataContext is not ViewModels.Rows.ReconciliationRow row)
            return;
        bool wanted = box.IsChecked == true;
        if (!await ViewModel.SetPlanEnabledAsync(row, wanted))
            box.IsChecked = !wanted;   // refused (e.g. override order) or failed — restore
    }

    // A 1:1 plan row's Desired NumberBox committed (focus left): if the integer actually changed, write it to the
    // TS db through the guarded path (which reloads on success); on a failed write or an empty/NaN box, snap the
    // box back to the row's current value. Only fires when the value differs, so re-focusing without editing — and
    // the binding settling the value on realization — write nothing.
    private async void Desired_Committed(object sender, RoutedEventArgs e)
    {
        if (sender is not NumberBox box || box.DataContext is not ViewModels.Rows.ReconciliationRow row)
            return;
        if (row.PlanTsKey is null) return;

        int current = row.Desired ?? 0;
        int wanted = double.IsNaN(box.Value) ? current : (int)System.Math.Round(box.Value);
        if (wanted == current) { box.Value = current; return; }

        if (!await ViewModel.SetPlanDesiredAsync(row, wanted))
            box.Value = current;   // write failed — restore the prior value
    }

    // ---- context-sensitive field editing (edit glyph + right-click → flyout) --------------------------------

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
            _ = ShowMosaicFlyoutAsync(el, group);
        else if (group.TsTargetKey is string key)
            _ = ShowEditFlyoutAsync(el, TsTable.Target, key, group.Target, group, null);
    }

    // A mosaic panel is a normal TS target — the standard target flyout, keyed by the panel's own row.
    private void EditPanelTarget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is PanelGroupRow { TsTargetKey: string key } panel)
            _ = ShowEditFlyoutAsync(el, TsTable.Target, key, $"{panel.Target} · {panel.Label}", null, null);
    }

    private void EditPlan_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is ReconciliationRow { PlanTsKey: string key } row)
            _ = ShowEditFlyoutAsync(el, TsTable.ExposurePlan, key, $"{row.Target} · {row.Filter}", null, row);
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
                        () => ShowEditFlyoutAsync(el, TsTable.Target, panelKey, $"{panel.Target} · {panel.Label}", null, null)));
                (projectKey, projectName) = (panel.Children[0].ProjectTsKey, panel.Children[0].Project);
                break;
            case ReconciliationRow row:
                if (row.PlanTsKey is string planKey)
                {
                    menu.Items.Add(EditMenuItem("Edit exposure plan…",
                        () => ShowEditFlyoutAsync(el, TsTable.ExposurePlan, planKey, $"{row.Target} · {row.Filter}", null, row)));
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
        };
        form.Children.Add(enableAll);

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
            };

            StackPanel priorityRow = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
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

        Flyout flyout = new() { Content = form, Placement = FlyoutPlacementMode.Bottom };
        flyout.ShowAt(anchor);
    }

    private static MenuFlyoutItem EditMenuItem(string text, Func<Task> open)
    {
        MenuFlyoutItem item = new() { Text = text, Icon = new FontIcon { Glyph = "" } };
        item.Click += (_, _) => _ = open();
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
        foreach (TemplateInfo template in templates)
            menu.Items.Add(EditMenuItem($"{template.Name} · {template.Filter} — used by {template.UsedByPlans} plan(s)",
                () => ShowEditFlyoutAsync((FrameworkElement)sender, TsTable.ExposureTemplate, template.TsKey,
                    TemplateTitle(template), null, null)));
        menu.ShowAt((FrameworkElement)sender);
    }

    private static string TemplateTitle(TemplateInfo template) =>
        $"Template '{template.Name}' — used by {template.UsedByPlans} plan(s)";

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
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 300,
        };

        UIElement content = TsFieldsEditor.Create(table, title, seed, async (column, value) =>
        {
            if (row is not null && string.Equals(column, "enabled", StringComparison.OrdinalIgnoreCase))
                return await ViewModel.SetPlanEnabledAsync(row, System.Convert.ToInt64(value) != 0);
            if (group is not null && string.Equals(column, "active", StringComparison.OrdinalIgnoreCase))
                return await ViewModel.SetTargetEnabledAsync(group, System.Convert.ToInt64(value) != 0);
            if (row is not null && string.Equals(column, "desired", StringComparison.OrdinalIgnoreCase))
                return await ViewModel.SetPlanDesiredAsync(row, System.Convert.ToInt32(value));
            if (row is not null && string.Equals(column, "exposure", StringComparison.OrdinalIgnoreCase))
            {
                // Seconds-cell mirror: the rounded override (0 is a literal zero-second exposure); only the
                // negative defer-to-template sentinel (null) resolves via the db.
                double v = System.Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
                return await ViewModel.SetPlanExposureAsync(row, v, v >= 0 ? (int)System.Math.Round(v) : null);
            }
            bool applied = await ViewModel.SetTsFieldAsync(table, key, column, value, title);
            if (applied && pairWarn is not null)
            {
                current[column] = value;
                RefreshPairWarn();
            }
            return applied;
        }, effective);

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

    private void ExpandAll_Click(object sender, RoutedEventArgs e) => ViewModel.ExpandAll();

    private void CollapseAll_Click(object sender, RoutedEventArgs e) => ViewModel.CollapseAll();

    // Ctrl+N: open (or focus) the diagnostics window — notes + screenshot into the tsm.log stream.
    private void Diagnostics_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        Support.DiagnosticsWindow.ShowOrFocus(this, ViewModel.GetDiagnosticsContext);
        args.Handled = true;
    }

    // ---- sync dialogs (push review + open-with-dirty) --------------------------------------------------------

    // The open-with-dirty prompt: unpushed edits exist and BIRDWATCHER is reachable, so the user decides
    // BEFORE any pull can overwrite them. Same review content as the push dialog, plus the Discard choice;
    // Escape/"Not now" keeps working locally with the journal intact (nothing is ever lost silently).
    private async Task<OpenDirtyDecision> ShowOpenDirtyDialogAsync(PushReview review)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = review.OldestEditAt is { } oldest
                ? $"Unpushed TS edits from {oldest.LocalDateTime:ddd HH:mm}"
                : "Unpushed TS edits",
            Content = BuildReviewContent(review),
            PrimaryButtonText = "Push to BIRDWATCHER",
            SecondaryButtonText = "Discard & pull fresh",
            CloseButtonText = "Not now",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => OpenDirtyDecision.Push,
            ContentDialogResult.Secondary => OpenDirtyDecision.Discard,
            _ => OpenDirtyDecision.ContinueLocal,
        };
    }

    // The push review: the one real decision, made with the collapsed journal on screen.
    private async Task<bool> ShowPushReviewDialogAsync(PushReview review)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = $"Push {review.CollapsedCount} field(s) to BIRDWATCHER",
            Content = BuildReviewContent(review),
            PrimaryButtonText = "Push",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    // Shared review body: staleness/busy facts up top, then the write-back count stamps (decreases first,
    // caution-colored — a 0 from a scan miss is the dangerous half), then the manual field edits.
    private static UIElement BuildReviewContent(PushReview review)
    {
        StackPanel panel = new() { Spacing = 10, MinWidth = 420 };

        if (review.RemoteBusy)
        {
            panel.Children.Add(new InfoBar
            {
                Title = "TS db busy on BIRDWATCHER",
                Message = "An open sidecar exists (NINA imaging?). The push will refuse until it closes.",
                Severity = InfoBarSeverity.Error,
                IsOpen = true,
                IsClosable = false,
            });
        }
        if (review.RemoteChangedSinceBaseline)
        {
            panel.Children.Add(new InfoBar
            {
                Title = "BIRDWATCHER changed since your last pull",
                Message = "NINA or XFM wrote to the TS db. The push replays only your edited fields; everything else stays untouched.",
                Severity = InfoBarSeverity.Warning,
                IsOpen = true,
                IsClosable = false,
            });
        }

        var caution = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
        if (review.WriteBack.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"Write-back — disk-count stamps ({review.WriteBack.Count})",
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            });
            foreach (PushReviewCountLine line in review.WriteBack)
            {
                // A desired-only raise has no count pair — show only what actually changed.
                string counts = line.NewCount is { } n ? $"TS {line.OldCount} → {n}" : "";
                string desired = line.NewDesired is { } d
                    ? $"{(counts.Length > 0 ? "  ·  " : "")}desired {line.OldDesired} → {d}"
                    : "";
                TextBlock text = new()
                {
                    Text = $"{(line.IsDecrease ? "▼  " : "")}{line.Label}  —  {counts}{desired}",
                    TextWrapping = TextWrapping.Wrap,
                };
                if (line.IsDecrease)
                    text.Foreground = caution;
                panel.Children.Add(text);
            }
        }

        if (review.Manual.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"Manual edits ({review.Manual.Count})",
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            });
            foreach (PushReviewFieldLine line in review.Manual)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"{line.Label}  —  {line.Column} {line.Old ?? "null"} → {line.New}",
                    TextWrapping = TextWrapping.Wrap,
                });
            }
        }

        return new ScrollViewer
        {
            Content = panel,
            MaxHeight = 480,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
    }

    // WinUI 3 NumberBox can't center its inner input via TextAlignment/HorizontalContentAlignment — the property
    // doesn't reach the template-internal TextBox (microsoft-ui-xaml#7399 / #2896). Set it on the realized
    // instance instead. Fires per container realization in the virtualized list; idempotent.
    private void DesiredBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is NumberBox box && FindDescendant<TextBox>(box) is TextBox input)
        {
            input.TextAlignment = TextAlignment.Center;
            input.MinWidth = 0;                          // default MinWidth can overflow a narrow box
            input.Padding = new Thickness(2, 0, 2, 0);   // trim inner padding so 3 digits fit centered when narrow
        }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is T nested) return nested;
        }
        return null;
    }
}
