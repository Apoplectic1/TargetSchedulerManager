using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace TargetSchedulerManager.App.Controls;

/// <summary>
/// Makes a popup surface (ContentDialog, flyout content) repositionable by dragging: WinUI centers dialogs
/// and anchors flyouts with no native way to move either, and both can cover exactly the grid rows the user
/// is comparing against. Pressing on a NON-interactive spot — the title, a label, blank space — and dragging
/// translates the surface; interactive controls are untouched because they handle PointerPressed themselves,
/// so the press never bubbles here. Mouse/pen/touch alike via pointer capture; coordinates are read in
/// window space (never relative to the moving element — that would feed the translation back into itself).
/// </summary>
internal static class DragMove
{
    public static void Attach(UIElement surface)
    {
        TranslateTransform translate = new();
        UIElement? target = null;

        // The element that must move is the visible CHROME, resolved lazily on first drag (the parent
        // chain exists only after the popup opens): flyout content lives inside a FlyoutPresenter —
        // translating the content alone slides it around INSIDE the stationary frame (field-verified
        // 2026-08-03) — so the presenter is the target; a surface with no presenter ancestor
        // (ContentDialog IS its own chrome) moves itself.
        UIElement ResolveTarget()
        {
            if (target is not null)
                return target;
            DependencyObject? node = surface;
            while (node is not null && node is not FlyoutPresenter)
                node = VisualTreeHelper.GetParent(node);
            target = node as UIElement ?? surface;
            target.RenderTransform = translate;
            return target;
        }

        bool dragging = false;
        Point last = default;

        surface.PointerPressed += (_, e) =>
        {
            Microsoft.UI.Input.PointerPoint point = e.GetCurrentPoint(null);
            if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse
                && !point.Properties.IsLeftButtonPressed)
                return;
            ResolveTarget();
            dragging = surface.CapturePointer(e.Pointer);
            last = point.Position;
        };
        surface.PointerMoved += (_, e) =>
        {
            if (!dragging)
                return;
            Point position = e.GetCurrentPoint(null).Position;
            translate.X += position.X - last.X;
            translate.Y += position.Y - last.Y;
            last = position;
        };
        surface.PointerReleased += (_, e) =>
        {
            dragging = false;
            surface.ReleasePointerCapture(e.Pointer);
        };
        surface.PointerCaptureLost += (_, _) => dragging = false;
    }
}
