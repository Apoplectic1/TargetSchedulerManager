using System.Reflection;
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
        // Version in the title (user obs 8573): MinVer's informational version, cut at the '+sha'
        // (an installed build reads "1.1.0"; an F5 build keeps its "-alpha…" shape, which is the
        // at-a-glance dev-vs-installed disambiguator).
        string version = (typeof(MainWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "?").Split('+')[0];
        Title = $"Target Scheduler Manager {version}";
        // Title-bar + taskbar icon; <ApplicationIcon> only stamps the exe, a WinUI 3 window needs this
        // runtime call. Absolute path: SetIcon resolves relative paths against the process CWD, not the exe.
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app_icon.ico"));
        // Width funds the grid: the ruler's fixed columns total ~1368 px (see GridColumns), and Target — the
        // one elastic column — must never truncate a real target name (user obs 27ec; longest today is
        // "Mosaic - Cygnus Loop · 16 panels" ≈ 250 px with its edit glyph). 1710 leaves Target ~300 px.
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1710, 980));

        // The sync dialogs (open-with-dirty, push review) are ContentDialogs and need a live XamlRoot, which
        // only exists once the window content has loaded — so the initial load waits for Loaded instead of
        // firing in the constructor.
        ViewModel.OpenWithDirtyPrompt = ShowOpenDirtyDialogAsync;
        ViewModel.ConfirmPushPrompt = ShowPushReviewDialogAsync;
        ViewModel.AdoptHoldPrompt = ShowAdoptHoldDialogAsync;
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

    private void CancelLoad_Click(object sender, RoutedEventArgs e) => ViewModel.CancelLoad();

    // Write + open the printable ambiguity report (fixes happen by hand in NINA's TS UI, never here).
    private void Ambiguities_Click(object sender, RoutedEventArgs e) => ViewModel.WriteAmbiguityReport();

    // One press, no confirm: enables/disables target.active by tonight's visibility (toolbar Duration
    // minutes 15–480 above Floor whole degrees 0–89), projects follow. The UpDownBox knobs commit any
    // typed text when the button steals focus and clamp to range, so both reads are valid whole numbers.
    private void VisibleTonight_Click(object sender, RoutedEventArgs e) =>
        FireAndLog(() => ViewModel.RunVisibleTonightAsync(
            TimeSpan.FromMinutes(VisibleDuration.Value), VisibleFloor.Value),
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

    // Narrow hidden-spinner NumberBoxes (the grid's Desired cells) route here. WinUI 3 can't center a
    // NumberBox's input via TextAlignment/HorizontalContentAlignment — the property doesn't reach the
    // template-internal TextBox (microsoft-ui-xaml#7399 / #2896) — and that TextBox's own MinWidth
    // otherwise overflows any narrow Width we set. Fix both on the realized instance. Fires per container
    // realization in the virtualized list; idempotent. (Inline-spinner NumberBoxes are a dead end at
    // narrow widths — the toolbar knobs are Controls/UpDownBox instead; DOMAIN.md → WinUI gotchas.)
    private void NarrowNumberBox_Loaded(object sender, RoutedEventArgs e)
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
