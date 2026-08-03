using Microsoft.UI.Xaml;
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
        surface.RenderTransform = translate;

        bool dragging = false;
        Point last = default;

        surface.PointerPressed += (_, e) =>
        {
            Microsoft.UI.Input.PointerPoint point = e.GetCurrentPoint(null);
            if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse
                && !point.Properties.IsLeftButtonPressed)
                return;
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
