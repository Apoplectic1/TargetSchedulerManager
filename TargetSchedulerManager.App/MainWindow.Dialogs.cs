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
        // Every dialog opens CENTERED (user call 2026-08-03, obs 3eba round 2: "just center any and all
        // dialogs") and is draggable from any non-interactive spot. Near-the-row open seeding is retired:
        // the ContentDialog element is a full-window overlay (generic.xaml: Container → smoke LayoutRoot →
        // the centered "BackgroundElement" box), and translating it against the anchor raced layout —
        // twice field-failed with the box off-screen, a modal you can't see eating every click.
        Controls.DragMove.Attach(dialog);
        // A lone dialog button centers (user 2026-08-05, obs f4d0). The template's CommandSpace is five
        // columns — Primary(*) · spacer · Secondary(0) · spacer · Close(*) — with every button
        // stretched, so a single visible button fills its half and reads off-center; 2–3 buttons fill
        // the whole row symmetrically and need nothing. All the knobs are per-instance settable after
        // the visibility states fire (unlike the NumberBox width saga), so this is a one-place repair.
        // Deferred one tick past Opened: at Opened the dialog's template children may not be reachable
        // from the element yet (the walk found nothing and the repair silently no-oped — field failure
        // obs c200); the enqueued pass runs after the tree is live. The DIAG line is the tripwire if
        // this ever regresses again.
        dialog.Opened += (d, _) => d.DispatcherQueue.TryEnqueue(() =>
        {
            string? lone = (d.PrimaryButtonText, d.SecondaryButtonText, d.CloseButtonText) switch
            {
                (not (null or ""), null or "", null or "") => "PrimaryButton",
                (null or "", not (null or ""), null or "") => "SecondaryButton",
                (null or "", null or "", not (null or "")) => "CloseButton",
                _ => null,
            };
            if (lone is null) return;   // 2-3 buttons fill the row symmetrically — nothing to repair
            if (FindDescendantByName(d, lone) is Button b)
            {
                Grid.SetColumn(b, 0);
                Grid.SetColumnSpan(b, 5);
                b.HorizontalAlignment = HorizontalAlignment.Center;
                b.MinWidth = 160;   // ~the half-column width it had; centered but still a dialog-scale target
            }
            else
            {
                Astronomy.Diagnostics.Log.Diag("Dialog", $"lone-button center: {lone} not found in visual tree");
            }
        });
        dialog.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.N   // Ctrl+N — see the accelerator gotcha above
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
        AssignmentRowControls assignment = new(facts.EmptyScopeReason, header: "Exposure template");
        panel.Children.Add(projectBox);
        panel.Children.Add(assignment.Box);
        panel.Children.Add(assignment.EmptyNote);
        panel.Children.Add(assignment.Caution);

        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "Add to TS",
            Content = panel,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        assignment.Changed += () => dialog.IsPrimaryButtonEnabled = assignment.Selected is not null;
        void RefreshTemplates()
        {
            AdoptionProjectOption option = facts.Projects[Math.Max(0, projectBox.SelectedIndex)];
            assignment.SetScope(option.Candidates, option.PreselectIndex);
        }
        projectBox.SelectionChanged += (_, _) => RefreshTemplates();
        RefreshTemplates();

        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary
            || projectBox.SelectedIndex < 0 || assignment.Selected is not { } chosen)
            return null;
        return new AdoptionChoice(facts.Projects[projectBox.SelectedIndex].Project, chosen.Template);
    }

    // The combined bulk-assignment dialog (openspec adopt-target-rollup): the project chosen once, then one
    // assignment row per eligible cell — include checkbox + the cell's disk facts + its own template combo
    // with the per-cell preselect and caution; a cell whose scope is empty in the chosen project's profile
    // renders greyed with the reason and is excluded. Switching the project swaps every row's precomputed
    // candidate list. Accept stays enabled while ≥1 included servable cell remains and returns exactly
    // those; Cancel writes nothing.
    private async Task<BulkAdoptionChoice?> ShowBulkAdoptDialogAsync(BulkAdoptionFacts facts)
    {
        int cellCount = facts.Cells.Count;
        string cells = cellCount == 1 ? "1 unplanned cell" : $"{cellCount} unplanned cells";
        StackPanel panel = new() { Spacing = 10, MinWidth = 460 };
        panel.Children.Add(new TextBlock
        {
            Text = facts.TargetName is string newName
                ? $"Create TS target \"{newName}\" from the disk centroid, plus a born-complete plan for "
                    + $"each included cell ({cells})."
                : $"Add a born-complete TS plan for each included cell of {facts.Label} ({cells}).",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 500,
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

        ComboBox projectBox = new()
        {
            Header = facts.ProjectLocked ? "Project (the target's — fixed)" : "Project",
            ItemsSource = facts.Projects.Select(o => o.Project.Name).ToList(),
            SelectedIndex = 0,
            IsEnabled = !facts.ProjectLocked,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        panel.Children.Add(projectBox);

        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = facts.TargetName is null ? "Add TS plans" : "Add to TS",
            Content = panel,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        List<(BulkAdoptionCell Cell, CheckBox Include, AssignmentRowControls Controls)> rows = [];
        void RefreshAccept() => dialog.IsPrimaryButtonEnabled =
            rows.Any(r => r.Include.IsChecked == true && r.Controls.Selected is not null);

        StackPanel cellList = new() { Spacing = 12 };
        foreach (BulkAdoptionCell cell in facts.Cells)
        {
            CheckBox include = new()
            {
                IsChecked = true,
                Content = $"{cell.Filter} ({cell.Purpose}) · {cell.DiskCount} × {cell.Seconds}s · "
                    + $"gain {cell.Gain}, offset {cell.Offset}, bin {cell.Bin}",
            };
            include.Checked += (_, _) => RefreshAccept();
            include.Unchecked += (_, _) => RefreshAccept();

            AssignmentRowControls controls = new(cell.EmptyScopeReason);
            controls.Changed += RefreshAccept;

            StackPanel detail = new() { Spacing = 4, Margin = new Thickness(28, 0, 0, 0) };
            detail.Children.Add(controls.Box);
            detail.Children.Add(controls.EmptyNote);
            detail.Children.Add(controls.Caution);
            StackPanel cellPanel = new() { Spacing = 4 };
            cellPanel.Children.Add(include);
            cellPanel.Children.Add(detail);
            cellList.Children.Add(cellPanel);
            rows.Add((cell, include, controls));
        }
        panel.Children.Add(new ScrollViewer
        {
            Content = cellList,
            MaxHeight = 380,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });

        void RefreshScopes()
        {
            BulkAdoptionProjectOption option = facts.Projects[Math.Max(0, projectBox.SelectedIndex)];
            for (int i = 0; i < rows.Count; i++)
            {
                BulkCellScope scope = option.CellScopes[i];
                rows[i].Controls.SetScope(scope.Candidates, scope.PreselectIndex);
                bool servable = scope.Candidates.Count > 0;
                rows[i].Include.IsEnabled = servable;
                if (!servable)
                    rows[i].Include.IsChecked = false;   // an unservable cell can't be included; re-checking after a project switch is the user's call
            }
            RefreshAccept();
        }
        projectBox.SelectionChanged += (_, _) => RefreshScopes();
        RefreshScopes();

        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary || projectBox.SelectedIndex < 0)
            return null;
        List<(ReconciliationRow, TsExposureTemplate)> assignments = [.. rows
            .Where(r => r.Include.IsChecked == true && r.Controls.Selected is not null)
            .Select(r => (r.Cell.Row, r.Controls.Selected!.Template))];
        if (assignments.Count == 0)
            return null;   // unreachable backstop — Accept is disabled in this state
        return new BulkAdoptionChoice(facts.Projects[projectBox.SelectedIndex].Project, assignments);
    }

    // One template-assignment control set — the candidate combo, the empty-scope note, and the non-pairing
    // caution — shared by the single-cell and bulk adoption dialogs so per-cell assignment behavior is
    // identical by construction (openspec adopt-target-rollup, design D3). The owner places the three
    // elements, feeds scopes through SetScope, and listens on Changed for Accept enablement.
    private sealed class AssignmentRowControls
    {
        private IReadOnlyList<AdoptionCandidate> _candidates = [];

        public AssignmentRowControls(string emptyReason, string? header = null)
        {
            Box = new ComboBox { Header = header, HorizontalAlignment = HorizontalAlignment.Stretch };
            EmptyNote = new TextBlock
            {
                Text = emptyReason,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 460,
                Visibility = Visibility.Collapsed,
            };
            Caution = new TextBlock
            {
                Foreground = ThemeBrushes.CautionText,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 460,
                Visibility = Visibility.Collapsed,
            };
            Box.SelectionChanged += (_, _) => { RefreshCaution(); Changed?.Invoke(); };
        }

        public ComboBox Box { get; }
        public TextBlock EmptyNote { get; }
        public TextBlock Caution { get; }

        /// <summary>Raised on any selection or scope change — the owner recomputes Accept enablement.</summary>
        public event Action? Changed;

        /// <summary>The selected candidate, or null when the scope is empty (Accept must not consume).</summary>
        public AdoptionCandidate? Selected =>
            Box.SelectedIndex >= 0 && Box.SelectedIndex < _candidates.Count ? _candidates[Box.SelectedIndex] : null;

        /// <summary>Swaps in one (cell × project) scope: candidates listed, best match preselected, the
        /// combo disabled and the empty note shown when the scope is empty.</summary>
        public void SetScope(IReadOnlyList<AdoptionCandidate> candidates, int preselectIndex)
        {
            _candidates = candidates;
            Box.ItemsSource = candidates.Select(c => CandidateText(c.Template)).ToList();
            Box.SelectedIndex = preselectIndex;
            Box.IsEnabled = candidates.Count > 0;
            EmptyNote.Visibility = candidates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshCaution();
            Changed?.Invoke();
        }

        private void RefreshCaution()
        {
            if (Selected is not { WouldPair: false } selected)
            {
                Caution.Visibility = Visibility.Collapsed;
                return;
            }
            Caution.Text = $"'{selected.Template.Name}' would not pair with these frames "
                + $"({selected.MismatchReason}) — the plan will appear as a separate TS row beside the "
                + "disk row, not merged into Both";
            Caution.Visibility = Visibility.Visible;
        }

        private static string CandidateText(TsExposureTemplate t) =>
            $"{t.Name} — {(t.Gain < 0 ? "camera-default gain" : $"gain {t.Gain}")}, "
            + $"{(t.Offset < 0 ? "camera-default offset" : $"offset {t.Offset}")}, bin {t.Bin}, "
            + $"default {t.DefaultExposure.ToString("0.#", CultureInfo.InvariantCulture)}s";
    }
}
