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
    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog, FrameworkElement? anchor = null)
    {
        // Drag any non-interactive spot to reposition (WinUI can't natively); with an anchor, the same
        // transform SEEDS the position so the dialog opens near the clicked row (the old flyout feel,
        // 2026-08-03) — clamped to the window, best-effort (centered is the graceful fallback).
        Microsoft.UI.Xaml.Media.TranslateTransform translate = Controls.DragMove.Attach(dialog);
        if (anchor is not null)
        {
            dialog.Opened += (_, _) =>
            {
                try
                {
                    Windows.Foundation.Point target = anchor.TransformToVisual(null)
                        .TransformPoint(new Windows.Foundation.Point(0, anchor.ActualHeight + 4));
                    Windows.Foundation.Point current = dialog.TransformToVisual(null)
                        .TransformPoint(new Windows.Foundation.Point(0, 0));
                    Windows.Foundation.Size root = dialog.XamlRoot.Size;
                    double x = Math.Clamp(target.X, 8, Math.Max(8, root.Width - dialog.ActualWidth - 8));
                    double y = Math.Clamp(target.Y, 8, Math.Max(8, root.Height - dialog.ActualHeight - 8));
                    translate.X = x - current.X;
                    translate.Y = y - current.Y;
                }
                catch (Exception ex)
                {
                    Astronomy.Diagnostics.Log.Warn($"dialog anchor seeding failed (stays centered): {ex.Message}");
                }
            };
        }
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

    // A structural adoption refusal (stale snapshot, missing centroid, no projects): the user explicitly
    // asked and the planner declined — an explicit action that silently declines deserves an explicit
    // answer, so the reason gets a dialog rather than only the status line (which a menu click leaves
    // unwatched).
    private async Task ShowAdoptRefusalDialogAsync(string reason)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "Add to TS — not added",
            Content = new TextBlock { Text = reason, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 },
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close,
        };
        await ShowDialogAsync(dialog);
    }

    // The row the adoption menu action was invoked from — seeds the assignment dialog's position (the
    // AdoptPrompt hook is wired once, but the anchor is per-invocation; AdoptRowFromMenuAsync sets it for
    // the duration of the call).
    private FrameworkElement? _adoptAnchor;

    // The assignment dialog every adoption goes through (openspec disk-row-adoption, user decision
    // 2026-08-03/obs 3dfe: assign existing templates, never create): project (locked to the owning project
    // when the TS target exists) + exposure template (strict same-filter/same-bin scope, best match
    // preselected), Accept/Cancel. No editable plan fields — the plan is born complete from disk facts and
    // any adjustment happens in the plan editor afterward. A non-pairing selection cautions inline (the
    // plan will land beside the disk row, not merge) but never blocks; an empty scope disables Accept with
    // the reason shown — the remedy (creating a template) belongs in TS.
    private async Task<AdoptionChoice?> ShowAdoptDialogAsync(AdoptionFacts facts)
    {
        StackPanel panel = new() { Spacing = 10, MinWidth = 420 };
        panel.Children.Add(new TextBlock
        {
            Text = facts.TargetName is string newName
                ? $"Create TS target \"{newName}\" from the disk centroid, plus one born-complete plan "
                    + $"(desired = acquired = {facts.DiskCount})."
                : $"Add one born-complete TS plan for {facts.Label} (desired = acquired = {facts.DiskCount}).",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 460,
        });
        if (facts is { RaHours: double ra, DecDegrees: double dec })
            panel.Children.Add(new TextBlock
            {
                Text = $"RA {ra.ToString("0.0000", CultureInfo.InvariantCulture)} h · "
                    + $"Dec {dec.ToString("+0.00;-0.00", CultureInfo.InvariantCulture)}°"
                    + (facts.SeededRotationDeg is double rot
                        ? $" · rotation {rot.ToString("0.#", CultureInfo.InvariantCulture)}° (from the frames' sky angle)"
                        : " · no rotation (none expressed on disk)"),
            });
        panel.Children.Add(new TextBlock
        {
            Text = $"Disk: {facts.DiskCount} × {facts.Seconds}s · {facts.Filter} ({facts.Purpose}) · "
                + $"gain {facts.Gain}, offset {facts.Offset}, bin {facts.Bin}",
            Opacity = 0.8,
        });

        ComboBox projectBox = new()
        {
            Header = facts.ProjectLocked ? "Project (the target's — fixed)" : "Project",
            ItemsSource = facts.Projects.Select(o => o.Project.Name).ToList(),
            SelectedIndex = 0,
            IsEnabled = !facts.ProjectLocked,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ComboBox templateBox = new()
        {
            Header = "Exposure template",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        TextBlock emptyNote = new()
        {
            Text = facts.EmptyScopeReason,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 460,
            Visibility = Visibility.Collapsed,
        };
        TextBlock caution = new()
        {
            Foreground = ThemeBrushes.CautionText,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 460,
            Visibility = Visibility.Collapsed,
        };
        panel.Children.Add(projectBox);
        panel.Children.Add(templateBox);
        panel.Children.Add(emptyNote);
        panel.Children.Add(caution);

        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "Add to TS",
            Content = panel,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        AdoptionProjectOption Option() => facts.Projects[Math.Max(0, projectBox.SelectedIndex)];
        static string CandidateText(TsExposureTemplate t) =>
            $"{t.Name} — {(t.Gain < 0 ? "camera-default gain" : $"gain {t.Gain}")}, "
            + $"{(t.Offset < 0 ? "camera-default offset" : $"offset {t.Offset}")}, bin {t.Bin}, "
            + $"default {t.DefaultExposure.ToString("0.#", CultureInfo.InvariantCulture)}s";

        void RefreshCaution()
        {
            AdoptionProjectOption option = Option();
            if (templateBox.SelectedIndex < 0 || templateBox.SelectedIndex >= option.Candidates.Count
                || option.Candidates[templateBox.SelectedIndex] is { WouldPair: true })
            {
                caution.Visibility = Visibility.Collapsed;
                return;
            }
            AdoptionCandidate selected = option.Candidates[templateBox.SelectedIndex];
            caution.Text = $"'{selected.Template.Name}' would not pair with these frames "
                + $"({selected.MismatchReason}) — the plan will appear as a separate TS row beside the "
                + "disk row, not merged into Both";
            caution.Visibility = Visibility.Visible;
        }
        void RefreshTemplates()
        {
            AdoptionProjectOption option = Option();
            templateBox.ItemsSource = option.Candidates.Select(c => CandidateText(c.Template)).ToList();
            templateBox.SelectedIndex = option.PreselectIndex;
            bool empty = option.Candidates.Count == 0;
            templateBox.IsEnabled = !empty;
            dialog.IsPrimaryButtonEnabled = !empty;
            emptyNote.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            RefreshCaution();
        }
        projectBox.SelectionChanged += (_, _) => RefreshTemplates();
        templateBox.SelectionChanged += (_, _) => RefreshCaution();
        RefreshTemplates();

        if (await ShowDialogAsync(dialog, _adoptAnchor) != ContentDialogResult.Primary
            || projectBox.SelectedIndex < 0 || templateBox.SelectedIndex < 0)
            return null;
        AdoptionProjectOption chosen = facts.Projects[projectBox.SelectedIndex];
        return new AdoptionChoice(chosen.Project, chosen.Candidates[templateBox.SelectedIndex].Template);
    }
}
