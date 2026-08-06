using Astronomy.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TargetSchedulerManager.App.Controls;

/// <summary>The app's <see cref="ContentDialog"/>: a lone command button centers (user 2026-08-05,
/// obs f4d0/c200). The template's CommandSpace is five columns — Primary(*) · spacer · Secondary(0) ·
/// spacer · Close(*) — with every button stretched, so a single visible button fills its half and reads
/// off-center; 2–3 buttons fill the whole row symmetrically and are left alone.
/// <para>Template-part route deliberately (obs c200): walking the visual tree from the dialog element
/// found nothing even a dispatcher tick after <c>Opened</c> — <see cref="FrameworkElement.OnApplyTemplate"/>
/// + <c>GetTemplateChild</c> is the contract API and cannot miss. Construct every app dialog as
/// <c>AppDialog</c>; the repair rides the type, not the show-site.</para></summary>
internal sealed class AppDialog : ContentDialog
{
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
