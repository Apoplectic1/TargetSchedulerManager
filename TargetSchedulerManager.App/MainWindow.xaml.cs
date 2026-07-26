using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;   // RepeatButton — the NumberBox spin buttons
using Microsoft.UI.Xaml.Media;
using TargetSchedulerManager.App.Shared;
using TargetSchedulerManager.App.ViewModels;
using static TargetSchedulerManager.App.Shared.UiTask;   // FireAndLog — the fire-and-forget seam (review N3)

namespace TargetSchedulerManager.App;

/// <summary>
/// Thin code-behind, WinForms-style: event handlers forward control state to the view-model, which owns all
/// logic. Display updates flow back through {x:Bind}/INotifyPropertyChanged rather than direct control writes.
/// <para><b>Partial layout (presentation split P2, 2026-07-24):</b> this core part holds the constructor,
/// the toolbar/grid event handlers, and the small view fix-ups; <c>MainWindow.Flyouts.cs</c> the edit
/// triggers, row context menu, Templates… picker, and the mosaic + schema-driven flyouts with their commit
/// routing; <c>MainWindow.Dialogs.cs</c> the sync ContentDialogs. One type — the split is navigational.</para>
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
            FireAndLog(() => ViewModel.LoadAsync(PullPolicy.IfChanged), "initial load");   // VM surfaces progress/errors via IsLoading/StatusText
        };
    }

    // Reload = rescan the disk + re-read the local db; it never pulls (pull is an open/Pull-now decision).
    private void Reload_Click(object sender, RoutedEventArgs e) =>
        FireAndLog(() => ViewModel.LoadAsync(PullPolicy.Never), "reload");

    private void Push_Click(object sender, RoutedEventArgs e) => FireAndLog(ViewModel.PushAsync, "push");

    private void PullNow_Click(object sender, RoutedEventArgs e) =>
        FireAndLog(() => ViewModel.LoadAsync(PullPolicy.Force), "pull-now");

    private void CancelPull_Click(object sender, RoutedEventArgs e) => ViewModel.CancelPull();

    // Write + open the printable ambiguity report (fixes happen by hand in NINA's TS UI, never here).
    private void Ambiguities_Click(object sender, RoutedEventArgs e) => ViewModel.WriteAmbiguityReport();

    // One press, no confirm: enables/disables target.active by tonight's visibility (toolbar Duration
    // minutes 15–480 above Floor whole degrees 0–89), projects follow. InvalidInputOverwritten has
    // restored any junk input by the time the click lands (the button steals focus first); the round
    // makes typed decimals whole (the floor is integer degrees by contract).
    private void VisibleTonight_Click(object sender, RoutedEventArgs e) =>
        FireAndLog(() => ViewModel.RunVisibleTonightAsync(
            TimeSpan.FromMinutes(Math.Round(VisibleDuration.Value)), (int)Math.Round(VisibleFloor.Value)),
            "visible-tonight");

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
    private void TargetEnable_Click(object sender, RoutedEventArgs e) => FireAndLog(async () =>
    {
        if (sender is not CheckBox box || box.DataContext is not ViewModels.Rows.TargetGroupRow group)
            return;
        bool now = box.IsChecked == true;
        if (!await ViewModel.SetTargetEnabledAsync(group, now))
            box.IsChecked = !now;   // write failed — restore the prior state
    }, "target enable toggle");

    // Per-plan enable: writes directly, no confirmation (user decision 2026-07-07) — the library clears the
    // target's cadence rows in the same transaction, TS regenerates the sequence from the new plan set on its
    // next pass (slot-0 restart accepted), and nothing reaches BIRDWATCHER until the reviewed push.
    private void PlanEnable_Click(object sender, RoutedEventArgs e) => FireAndLog(async () =>
    {
        if (sender is not CheckBox box || box.DataContext is not ViewModels.Rows.ReconciliationRow row)
            return;
        bool wanted = box.IsChecked == true;
        if (!await ViewModel.SetPlanEnabledAsync(row, wanted))
            box.IsChecked = !wanted;   // refused (e.g. override order) or failed — restore
    }, "plan enable toggle");

    // Inline Desired commits serialize like the flyout's (openspec serial-commits): two rapid confirms
    // used to overlap their write+verify — the first's read-back could see the second's value and
    // spuriously revert a good edit. One chain for all Desired boxes (cross-row serialization costs ms).
    private readonly CommitChain _desiredCommits = new();

    // A 1:1 plan row's Desired NumberBox committed (focus left): if the integer actually changed, write it to the
    // TS db through the guarded path (which mirrors the row in place on success); on a failed write or an
    // empty/NaN box, snap the box back to the row's current value. Only fires when the value differs, so
    // re-focusing without editing — and the binding settling the value on realization — write nothing.
    private void Desired_Committed(object sender, RoutedEventArgs e) => FireAndLog(async () =>
    {
        if (sender is not NumberBox box || box.DataContext is not ViewModels.Rows.ReconciliationRow row)
            return;
        if (row.PlanTsKey is null) return;

        int current = row.Desired ?? 0;
        int wanted = double.IsNaN(box.Value) ? current : (int)System.Math.Round(box.Value);
        if (wanted == current) { box.Value = current; return; }

        if (!await _desiredCommits.Run(() => ViewModel.SetPlanDesiredAsync(row, wanted)))
            box.Value = current;   // write failed — restore the prior value
    }, "inline Desired commit");

    private void ExpandAll_Click(object sender, RoutedEventArgs e) => ViewModel.ExpandAll();

    private void CollapseAll_Click(object sender, RoutedEventArgs e) => ViewModel.CollapseAll();

    // Ctrl+N: open (or focus) the diagnostics window — notes + screenshot into the tsm.log stream.
    private void Diagnostics_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        Support.DiagnosticsWindow.ShowOrFocus(this, ViewModel.GetDiagnosticsContext);
        args.Handled = true;
    }

    // Narrow NumberBoxes (the grid's Desired cell, the Visible-Tonight knobs) route here for the template
    // fix-ups XAML can't express — properties don't reach a NumberBox's internals (microsoft-ui-xaml#7399
    // / #2896), and a narrow control isn't a mode the template supports:
    //  - Zero the inner TextBox MinWidth (the template forces 120 with inline spinners, 64 without) —
    //    the one thing that lets a narrow Width stick at all.
    //  - Inline spinners: shrink the chevron pair (stock 76 px) and right-pad the text clear of it. The
    //    template draws the buttons ON TOP of the TextBox with no reservation — stock layout survives
    //    only because the 120 px minimum keeps short left-aligned text away from them.
    //  - Hidden spinners (grid cells): center the digits per the integer-edit-box rule. (Toolbar boxes
    //    stay left-aligned, WinForms-up-down style — centering across the full box is what put digits
    //    under the chevrons, obs-1fe4.)
    // Fires per container realization in the virtualized list; idempotent.
    private void NarrowNumberBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not NumberBox box) return;
        bool inlineSpinners = box.SpinButtonPlacementMode == NumberBoxSpinButtonPlacementMode.Inline;

        if (FindDescendant<TextBox>(box) is TextBox input)
        {
            input.MinWidth = 0;
            if (inlineSpinners)
                input.Padding = new Thickness(4, 0, 38, 0);   // keep digits clear of the 36 px chevron pair
            else
            {
                input.TextAlignment = TextAlignment.Center;
                input.Padding = new Thickness(2, 0, 2, 0);    // trim so 3 digits fit centered when narrow
            }
        }

        if (!inlineSpinners) return;
        foreach (RepeatButton spin in FindDescendants<RepeatButton>(box))
        {
            // 16 px wide (stock 32), 2 px outer margins (stock 4) — full height, just narrower to hit.
            if (spin.Name == "UpSpinButton")
                (spin.MinWidth, spin.Margin) = (16, new Thickness(2, 2, 0, 2));
            else if (spin.Name == "DownSpinButton")
                (spin.MinWidth, spin.Margin) = (16, new Thickness(0, 2, 2, 2));
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

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (T nested in FindDescendants<T>(child)) yield return nested;
        }
    }
}
