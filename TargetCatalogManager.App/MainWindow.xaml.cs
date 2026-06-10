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
}
