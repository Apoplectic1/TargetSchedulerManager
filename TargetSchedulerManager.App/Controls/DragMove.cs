using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace TargetSchedulerManager.App.Controls;

/// <summary>
/// Makes a ContentDialog repositionable by dragging: WinUI centers dialogs with no native way to move
/// them, and a dialog can cover exactly the grid rows the user is comparing against. Pressing on a
/// NON-interactive spot — the title, a label, blank space — and dragging translates the dialog;
/// interactive controls are untouched because they handle PointerPressed themselves, so the press never
/// bubbles here. Returns the transform so the caller can SEED a position (open-near-the-row).
/// <para>Dialogs only, by design (2026-08-03): a flyout renders in its own top-level popup window whose
/// placement the Flyout owns — content/presenter transforms and Popup offset writes all failed in the
/// field, so form-hosting surfaces are dialogs now and menus (where movability is meaningless) stay
/// flyouts. Drag deltas are read in window space — element-relative coordinates would feed the
/// translation back into itself.</para>
/// </summary>
internal static class DragMove
{
    public static TranslateTransform Attach(UIElement surface)
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

        return translate;
    }
}
