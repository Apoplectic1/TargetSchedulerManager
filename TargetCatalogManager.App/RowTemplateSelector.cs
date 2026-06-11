using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TargetCatalogManager.App.Models;

namespace TargetCatalogManager.App;

/// <summary>
/// Picks the group-header or filter-row template per item in the flattened rows list. This is WinUI's
/// per-item template dispatch — the declarative equivalent of a WinForms owner-draw "which renderer?"
/// switch, resolved once when the item's container is created.
/// </summary>
public sealed partial class RowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? GroupTemplate { get; set; }
    public DataTemplate? PanelTemplate { get; set; }
    public DataTemplate? FilterTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item switch
    {
        TargetGroupRow => GroupTemplate,
        PanelGroupRow => PanelTemplate,
        _ => FilterTemplate,
    };

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
