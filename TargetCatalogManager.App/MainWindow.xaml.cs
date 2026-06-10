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
        Title = "Target Catalog Manager — TS plan vs disk (M1, read-only)";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1560, 980));
        _ = ViewModel.LoadAsync();   // initial load; VM surfaces progress/errors via IsLoading/StatusText
    }

    private void Reload_Click(object sender, RoutedEventArgs e) => _ = ViewModel.LoadAsync();

    private void Search_TextChanged(object sender, TextChangedEventArgs e) =>
        ViewModel.SearchText = SearchBox.Text;

    private void SourceFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.SourceFilterIndex = SourceFilter.SelectedIndex;

    private void FlaggedOnly_Changed(object sender, RoutedEventArgs e) =>
        ViewModel.FlaggedOnly = FlaggedOnly.IsChecked == true;

    private void Sort_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.SortMode = (SortMode)SortPicker.SelectedIndex;

    // Whole-row click toggles a disclosure: target group headers and mixed-seconds rollups; clicks on
    // plain one-plane rows fall through.
    private void Row_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Models.TargetGroupRow group)
            ViewModel.ToggleGroup(group);
        else if (e.ClickedItem is Models.ReconciliationRow { Detail: not null } rollup)
            ViewModel.ToggleRollup(rollup);
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
