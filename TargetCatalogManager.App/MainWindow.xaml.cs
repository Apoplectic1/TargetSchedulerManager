using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TargetCatalogManager.App.ViewModels;

namespace TargetCatalogManager.App;

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
        Title = "Target Catalog Manager — TS plan vs disk (editing: target enable)";
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

    private void ExpandAll_Click(object sender, RoutedEventArgs e) => ViewModel.ExpandAll();

    private void CollapseAll_Click(object sender, RoutedEventArgs e) => ViewModel.CollapseAll();

    // Ctrl+N: open (or focus) the observation window — notes + screenshot into the tcm.log stream.
    private void Observation_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        Support.ObservationWindow.ShowOrFocus(this, ViewModel.GetObservationContext);
        args.Handled = true;
    }
}
