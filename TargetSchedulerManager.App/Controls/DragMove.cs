using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace TargetSchedulerManager.App.Controls;

/// <summary>
/// Makes a popup surface (ContentDialog, flyout content) repositionable by dragging: WinUI centers dialogs
/// and anchors flyouts with no native way to move either, and both can cover exactly the grid rows the user
/// is comparing against. Pressing on a NON-interactive spot — the title, a label, blank space — and dragging
/// moves the surface; interactive controls are untouched because they handle PointerPressed themselves, so
/// the press never bubbles here.
/// <para>
/// Two mechanisms, resolved lazily on first drag (the parent chain exists only after the popup opens),
/// because the two surfaces are hosted differently (all field-verified 2026-08-03):
/// a <b>flyout</b> renders in its own top-level popup window, so no XAML transform can move it on screen —
/// translating the content slid it inside a stationary frame, translating the presenter pinned it against
/// its own window's bounds; the <see cref="Popup"/>'s offsets are what move the popup window itself. A
/// <b>ContentDialog</b> is its own chrome inside the main window's tree, where a TranslateTransform works.
/// Drag math differs per mechanism: dialog pointer coords live in the static main-window frame
/// (incremental, last-point updated per move); a windowed popup's coords live in the popup's OWN moving
/// frame — the frame chases the applied offset, so each reading is the increment since the frame caught up
/// and the grab point is never updated (updating it would subtract every applied delta from the next one).
/// </para>
/// </summary>
internal static class DragMove
{
    public static void Attach(UIElement surface)
    {
        TranslateTransform translate = new();
        Popup? popup = null;
        bool resolved = false;
        bool dragging = false;
        Point grab = default;

        void Resolve()
        {
            if (resolved)
                return;
            resolved = true;
            DependencyObject? node = surface;
            while (node is not null && node is not FlyoutPresenter)
                node = VisualTreeHelper.GetParent(node);
            if (node is FlyoutPresenter presenter && surface.XamlRoot is { } xamlRoot)
                popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot)
                    .FirstOrDefault(p => ReferenceEquals(p.Child, presenter));
            if (popup is null)
                surface.RenderTransform = translate;   // dialog: its own chrome, main-window frame
        }

        surface.PointerPressed += (_, e) =>
        {
            Microsoft.UI.Input.PointerPoint point = e.GetCurrentPoint(null);
            if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse
                && !point.Properties.IsLeftButtonPressed)
                return;
            Resolve();
            dragging = surface.CapturePointer(e.Pointer);
            grab = point.Position;
        };
        surface.PointerMoved += (_, e) =>
        {
            if (!dragging)
                return;
            Point position = e.GetCurrentPoint(null).Position;
            if (popup is not null)
            {
                // Moving frame: the popup window follows each applied delta, so the reading returns to
                // the grab point between moves — (position − grab) IS the increment. Never update grab.
                popup.HorizontalOffset += position.X - grab.X;
                popup.VerticalOffset += position.Y - grab.Y;
            }
            else
            {
                translate.X += position.X - grab.X;
                translate.Y += position.Y - grab.Y;
                grab = position;   // static frame: classic incremental tracking
            }
        };
        surface.PointerReleased += (_, e) =>
        {
            dragging = false;
            surface.ReleasePointerCapture(e.Pointer);
        };
        surface.PointerCaptureLost += (_, _) => dragging = false;
    }
}
