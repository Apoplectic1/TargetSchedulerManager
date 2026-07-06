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

    public MainWindow()
    {
        InitializeComponent();
        Title = "Target Scheduler Manager — TS plan vs disk (editing: target enable)";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1560, 980));
        _ = ViewModel.LoadAsync();   // initial load; VM surfaces progress/errors via IsLoading/StatusText
    }

    private void Reload_Click(object sender, RoutedEventArgs e) => _ = ViewModel.LoadAsync();

    // LIVE/LOCAL radios pick the TS db. Checked fires on user selection (and on the IsChecked binding settling),
    // but SetTsMode no-ops when the mode is unchanged, so binding-driven re-checks don't reload.
    private void Live_Checked(object sender, RoutedEventArgs e) => ViewModel.SetTsMode(TsMode.Live);

    private void Local_Checked(object sender, RoutedEventArgs e) => ViewModel.SetTsMode(TsMode.Local);

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
        if (sender is FrameworkElement el && el.DataContext is TargetGroupRow { TsTargetKey: string key } group)
            _ = ShowEditFlyoutAsync(el, TsTable.Target, key, group.Target, group, null);
    }

    private void EditPlan_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is ReconciliationRow { PlanTsKey: string key } row)
            _ = ShowEditFlyoutAsync(el, TsTable.ExposurePlan, key, $"{row.Target} · {row.Filter}", null, row);
    }

    // Right-click anywhere on a TS-backed row: a context menu whose items are gated by the row's data — the
    // extension point for future per-row actions (template editing, cadence actions). Handled centrally on the
    // ListView so the templates stay menu-free.
    private void Row_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement el)
            return;

        MenuFlyout menu = new();
        switch (el.DataContext)
        {
            case TargetGroupRow { CanEnable: true, TsTargetKey: string key } group:
                menu.Items.Add(EditMenuItem("Edit target…",
                    () => ShowEditFlyoutAsync(el, TsTable.Target, key, group.Target, group, null)));
                break;
            case ReconciliationRow { PlanTsKey: string key } row:
                menu.Items.Add(EditMenuItem("Edit exposure plan…",
                    () => ShowEditFlyoutAsync(el, TsTable.ExposurePlan, key, $"{row.Target} · {row.Filter}", null, row)));
                break;
            default:
                return;   // disk-only rows, panels, rollups: no menu
        }

        menu.ShowAt(el, new FlyoutShowOptions { Position = e.GetPosition(el) });
        e.Handled = true;
    }

    private static MenuFlyoutItem EditMenuItem(string text, Func<Task> open)
    {
        MenuFlyoutItem item = new() { Text = text, Icon = new FontIcon { Glyph = "" } };
        item.Click += (_, _) => _ = open();
        return item;
    }

    // Seeds the schema-driven form from the current db (off the UI thread), then shows it in a flyout anchored
    // at the gesture's row. Every field commits itself through the guarded gate; fields with dedicated in-grid
    // controls route through their specific setters so their cells refresh in place. Per-field commit means
    // light-dismiss can never lose work — no confirmation needed on close.
    private async Task ShowEditFlyoutAsync(
        FrameworkElement anchor, TsTable table, string key, string title,
        TargetGroupRow? group, ReconciliationRow? row)
    {
        IReadOnlyDictionary<string, object?>? seed = await ViewModel.ReadTsFieldsAsync(table, key, title);
        UIElement content = TsFieldsEditor.Create(table, title, seed, async (column, value) =>
        {
            if (group is not null && string.Equals(column, "active", StringComparison.OrdinalIgnoreCase))
                return await ViewModel.SetTargetEnabledAsync(group, System.Convert.ToInt64(value) != 0);
            if (row is not null && string.Equals(column, "desired", StringComparison.OrdinalIgnoreCase))
                return await ViewModel.SetPlanDesiredAsync(row, System.Convert.ToInt32(value));
            return await ViewModel.SetTsFieldAsync(table, key, column, value, title);
        });

        Flyout flyout = new() { Content = content, Placement = FlyoutPlacementMode.Bottom };
        flyout.ShowAt(anchor);
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
