using Astronomy.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TargetSchedulerManager.App.Controls;

/// <summary>The app's dialog type — construct every dialog as <c>AppDialog</c>, never raw
/// <see cref="ContentDialog"/>. Every per-dialog behavior rides the type so it is true by construction
/// (openspec dialog-behaviors-on-type; the funnel-comment convention drifted — the update prompt
/// bypassed it and silently lacked drag + Ctrl+N):
/// <list type="bullet">
/// <item>drag-to-move from any non-interactive spot (<see cref="DragMove"/>, attached in the ctor);</item>
/// <item>Ctrl+N diagnostics capture (<see cref="DiagnosticsHook"/> — set once by the window; an unset
/// hook degrades to no capture, never a crash);</item>
/// <item>a lone command button centers (user 2026-08-05, obs f4d0/c200): the template's CommandSpace is
/// five columns — Primary(*) · spacer · Secondary(0) · spacer · Close(*) — every button stretched, so a
/// single visible button fills its half and reads off-center; 2–3 buttons fill the whole row
/// symmetrically and are left alone.</item>
/// </list>
/// <para>Template-part route deliberately (obs c200): walking the visual tree from the dialog element
/// found nothing even a dispatcher tick after <c>Opened</c> — <see cref="FrameworkElement.OnApplyTemplate"/>
/// + <c>GetTemplateChild</c> is the contract API and cannot miss.</para></summary>
internal sealed class AppDialog : ContentDialog
{
    /// <summary>The Ctrl+N diagnostics action, set once at window construction. Static deliberately:
    /// single-window app, and the window context it captures must not leak into this Controls-layer
    /// type. PreviewKeyDown is the reliable route — the dialog's popup ignores KeyboardAccelerators
    /// entirely (microsoft-ui-xaml #2408 family; field-verified 2026-08-03).</summary>
    public static Action? DiagnosticsHook { get; set; }

    public AppDialog()
    {
        DragMove.Attach(this);
        PreviewKeyDown += (_, e) =>
        {
            if (DiagnosticsHook is null
                || e.Key != Windows.System.VirtualKey.N
                || !Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                    .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
                return;
            DiagnosticsHook();
            e.Handled = true;
        };
    }
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        string? lone = (PrimaryButtonText, SecondaryButtonText, CloseButtonText) switch
        {
            (not (null or ""), null or "", null or "") => "PrimaryButton",
            (null or "", not (null or ""), null or "") => "SecondaryButton",
            (null or "", null or "", not (null or "")) => "CloseButton",
            _ => null,
        };
        if (lone is null) return;   // 2–3 buttons fill the row symmetrically — nothing to repair
        if (GetTemplateChild(lone) is Button b)
        {
            Grid.SetColumn(b, 0);
            Grid.SetColumnSpan(b, 5);
            b.HorizontalAlignment = HorizontalAlignment.Center;
            b.MinWidth = 160;   // ~the half-column width it had; centered but still a dialog-scale target
        }
        else
        {
            Log.Diag("Dialog", $"lone-button center: template part {lone} not found");
        }
    }
}
