using System.Globalization;
using Astronomy.Catalog.TargetScheduler;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TargetSchedulerManager.App.Models;
using TargetSchedulerManager.App.Services;
using TargetSchedulerManager.App.Shared;
using TargetSchedulerManager.App.ViewModels;
using TargetSchedulerManager.App.ViewModels.Rows;

namespace TargetSchedulerManager.App;

// The sync dialogs (presentation split P2): the open-with-dirty prompt, the push review, and their
// shared review body. Wired to the view-model's UI hooks in the core part's constructor.
public sealed partial class MainWindow
{
    // An open ContentDialog swallows the window-level Ctrl+N — precisely when a diagnostics capture
    // matters most (an observation OF the dialog). And a KeyboardAccelerator attached to the dialog is
    // ignored too: the dialog's inner popup doesn't participate in accelerator collection at all (the
    // long-standing microsoft-ui-xaml #2408 family; field-verified here 2026-08-03 — the accelerator
    // variant shipped and did nothing). PreviewKeyDown is the reliable route: it tunnels through the
    // dialog before its children see the key, independent of the accelerator plumbing. Every dialog
    // shows through here.
    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        dialog.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.N
                || !Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                    .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
                return;
            Support.DiagnosticsWindow.ShowOrFocus(this, ViewModel.GetDiagnosticsContext);
            e.Handled = true;
        };
        return await dialog.ShowAsync();
    }

    // The open-with-dirty prompt: unpushed edits exist and BIRDWATCHER is reachable, so the user decides
    // BEFORE any pull can overwrite them. Same review content as the push dialog, plus the Discard choice;
    // Escape/"Not now" keeps working locally with the journal intact (nothing is ever lost silently).
    private async Task<OpenDirtyDecision> ShowOpenDirtyDialogAsync(PushReview review)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = review.OldestEditAt is { } oldest
                ? $"Unpushed TS edits from {oldest.LocalDateTime:ddd HH:mm}"
                : "Unpushed TS edits",
            Content = BuildReviewContent(review),
            PrimaryButtonText = "Push to BIRDWATCHER",
            SecondaryButtonText = "Discard & pull fresh",
            CloseButtonText = "Not now",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await ShowDialogAsync(dialog) switch
        {
            ContentDialogResult.Primary => OpenDirtyDecision.Push,
            ContentDialogResult.Secondary => OpenDirtyDecision.Discard,
            _ => OpenDirtyDecision.ContinueLocal,
        };
    }

    // The push review: the one real decision, made with the collapsed journal on screen.
    private async Task<bool> ShowPushReviewDialogAsync(PushReview review)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = $"Push {review.CollapsedCount} field(s) to BIRDWATCHER",
            Content = BuildReviewContent(review),
            PrimaryButtonText = "Push",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await ShowDialogAsync(dialog) == ContentDialogResult.Primary;
    }

    // Shared review body: staleness/busy facts up top, then the rows the push will CREATE (adoptions — a row
    // coming into existence on BIRDWATCHER outranks a field change in review weight), then the write-back
    // count stamps (decreases first, caution-colored — a 0 from a scan miss is the dangerous half), then the
    // manual field edits.
    private static UIElement BuildReviewContent(PushReview review)
    {
        StackPanel panel = new() { Spacing = 10, MinWidth = 420 };

        if (review.RemoteBusy)
        {
            panel.Children.Add(new InfoBar
            {
                Title = "TS db busy on BIRDWATCHER",
                Message = "An open sidecar exists (NINA imaging?). The push will refuse until it closes.",
                Severity = InfoBarSeverity.Error,
                IsOpen = true,
                IsClosable = false,
            });
        }
        if (review.RemoteChangedSinceBaseline)
        {
            panel.Children.Add(new InfoBar
            {
                Title = "BIRDWATCHER changed since your last pull",
                Message = "NINA or XFM wrote to the TS db. The push replays only your edited fields; everything else stays untouched.",
                Severity = InfoBarSeverity.Warning,
                IsOpen = true,
                IsClosable = false,
            });
        }

        if (review.Creates.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"Creates — new TS rows ({review.Creates.Count})",
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            });
            foreach (PushReviewCreateLine line in review.Creates)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"＋ {line.Label}  —  new {line.Entity}: {line.Summary}",
                    TextWrapping = TextWrapping.Wrap,
                });
            }
        }

        Brush? caution = ThemeBrushes.CautionText;
        if (review.WriteBack.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"Write-back — disk-count stamps ({review.WriteBack.Count})",
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            });
            foreach (PushReviewCountLine line in review.WriteBack)
            {
                // A desired-only raise has no count pair — show only what actually changed.
                string counts = line.NewCount is { } n ? $"TS {line.OldCount} → {n}" : "";
                string desired = line.NewDesired is { } d
                    ? $"{(counts.Length > 0 ? "  ·  " : "")}desired {line.OldDesired} → {d}"
                    : "";
                TextBlock text = new()
                {
                    Text = $"{(line.IsDecrease ? "▼  " : "")}{line.Label}  —  {counts}{desired}",
                    TextWrapping = TextWrapping.Wrap,
                };
                if (line.IsDecrease)
                    text.Foreground = caution;
                panel.Children.Add(text);
            }
        }

        if (review.Manual.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"Manual edits ({review.Manual.Count})",
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            });
            foreach (PushReviewFieldLine line in review.Manual)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"{line.Label}  —  {line.Column} {line.Old ?? "null"} → {line.New}",
                    TextWrapping = TextWrapping.Wrap,
                });
            }
        }

        return new ScrollViewer
        {
            Content = panel,
            MaxHeight = 480,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
    }

    // An adoption hold: the user explicitly asked and the planner declined — an explicit action that
    // silently declines deserves an explicit answer, so the reason gets a dialog rather than only the
    // status line (which a menu click leaves unwatched). A zero-template-match hold carries a create
    // offer: the primary button mints the missing template from the cell's numbers (policy fields cloned
    // from the named donor) and adopts in one atomic batch. Returns true exactly when the offer is taken.
    private async Task<bool> ShowAdoptHoldDialogAsync(AdoptionHold hold)
    {
        StackPanel panel = new() { Spacing = 10, MaxWidth = 480 };
        panel.Children.Add(new TextBlock { Text = hold.Message, TextWrapping = TextWrapping.Wrap });
        if (hold.Offer is { } offer)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"TSM can create template \"{offer.ProposedName}\" (gain {offer.Gain}, offset {offer.Offset}, "
                    + $"bin {offer.Bin}, default {offer.Seconds}s) — other settings cloned from '{offer.DonorName}' — "
                    + "and add the plan with it.",
                TextWrapping = TextWrapping.Wrap,
            });
        }
        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "Add to TS — not added",
            Content = panel,
            PrimaryButtonText = hold.Offer is null ? null : "Create template + add plan",
            CloseButtonText = hold.Offer is null ? "OK" : "Cancel",
            DefaultButton = hold.Offer is null ? ContentDialogButton.Close : ContentDialogButton.Primary,
        };
        return await ShowDialogAsync(dialog) == ContentDialogResult.Primary && hold.Offer is not null;
    }

    // The target-creating adoption's project-picker/confirm dialog (openspec disk-row-adoption): the disk
    // target's facts (name, plate-solved centroid in TS units, the rotation a sky framing seeds) plus a
    // picker over the existing non-mosaic projects — projects are never created here. Returns the picked
    // project, or null on cancel (nothing is written then; the caller only acts on a pick).
    private async Task<TsProject?> ShowAdoptTargetDialogAsync(ReconciliationRow row)
    {
        IReadOnlyList<TsProject> projects = ViewModel.AdoptableProjects();
        if (projects.Count == 0)
        {
            ViewModel.NoteStatus("no TS projects to adopt into — create one in NINA's TS editor first");
            return null;
        }
        if (ViewModel.GetAdoptionTargetFacts(row) is not { } facts)
        {
            ViewModel.NoteStatus($"can't add {Format.Label(row.Target, row.Filter)} to TS — no plate-solved centroid on disk");
            return null;
        }
        (string name, double ra, double dec, double? rotation) = facts;

        StackPanel panel = new() { Spacing = 10, MinWidth = 380 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Create TS target \"{name}\" from the disk centroid, plus one born-complete plan for "
                + $"{Format.Label(row.Filter, $"{row.DiskSeconds}s")} (desired = acquired = {row.Disk}).",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"RA {ra.ToString("0.0000", CultureInfo.InvariantCulture)} h · "
                + $"Dec {dec.ToString("+0.00;-0.00", CultureInfo.InvariantCulture)}°"
                + (rotation is double rot
                    ? $" · rotation {rot.ToString("0.#", CultureInfo.InvariantCulture)}° (from the frames' sky angle)"
                    : " · no rotation (none expressed on disk)"),
        });
        ComboBox picker = new()
        {
            Header = "Project",
            ItemsSource = projects.Select(p => p.Name).ToList(),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        panel.Children.Add(picker);

        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "Add to TS",
            Content = panel,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await ShowDialogAsync(dialog) == ContentDialogResult.Primary && picker.SelectedIndex >= 0
            ? projects[picker.SelectedIndex]
            : null;
    }
}
