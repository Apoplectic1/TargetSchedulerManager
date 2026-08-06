# Design: dialog-behaviors-on-type

## Context

See `proposal.md` — Why. Today: `AppDialog` (Controls) owns lone-button centering via
`OnApplyTemplate`/`GetTemplateChild`; `ShowDialogAsync` (MainWindow.Dialogs.cs) attaches DragMove and
the Ctrl+N PreviewKeyDown hook per call; `UpdateService` bypasses the funnel entirely. All 8
construction sites are already typed `AppDialog` (2026-08-05, obs c200 fix).

## Goals / Non-Goals

**Goals**
- Every per-dialog behavior true by construction on the type; zero behaviors enforced by comment.
- Compile-time rejection of a raw `ContentDialog` reaching the funnel.

**Non-Goals**
- No construction factory (option C, rejected): sites wire dialogs after construction
  (IsPrimaryButtonEnabled toggling, result mapping), so a factory sprouts parameters while removing
  only the XamlRoot line. Not worth the ceremony.
- No visual changes to any dialog beyond the update prompt gaining drag + Ctrl+N.

## Decisions

1. **DragMove attaches in the `AppDialog` constructor** — `Controls.DragMove.Attach(this)`. It only
   hooks events, safe pre-template.
2. **Ctrl+N via a static hook**: `AppDialog.DiagnosticsHook` (`static Action?`), set once by
   `MainWindow` at construction; `AppDialog` wires PreviewKeyDown in its constructor and invokes the
   hook when set. Rationale: the hook needs window context (`DiagnosticsWindow.ShowOrFocus(this,
   ViewModel.GetDiagnosticsContext)`) that a Controls-layer type must not know. A static is honest
   here — one window exists (single-window app), and a null hook degrades to "no diagnostics capture",
   never a crash. Rejected: per-instance injection (8 sites re-grow the boilerplate the change
   removes); keeping Ctrl+N in the funnel (leaves the update prompt uncovered — the observed drift).
3. **`ShowDialogAsync(Controls.AppDialog dialog)`** — signature narrows; body shrinks to the await
   (+ the existing centered/movable doc comment). The funnel stays as the single show seam (result
   mapping call sites keep reading naturally); it just no longer carries behavior.
4. **UpdateService stays funnel-free** — its prompt is owned by a service without MainWindow access;
   with behaviors on the type, the direct `ShowAsync` is no longer a gap. No `owner` plumbing added.

## Risks / Trade-offs

- [Static hook is process-global state] → single-window app, set once at startup, read-only after;
  the alternative (plumbing a window reference into Controls) couples the layer worse.
- [A future dialog constructed as raw `ContentDialog` and shown via `ShowAsync` directly would skip
  everything] → the funnel's narrowed signature catches funnel users; UI.md's "app dialogs are
  AppDialog, never raw ContentDialog" rule (already written, obs c200) covers the rest.

## Migration Plan

Pure app-side refactor + one small behavior gain (update-prompt drag/Ctrl+N). Ship; the update prompt
is hard to field-verify on demand (needs a pending release), so verification is: editor dialogs still
drag + Ctrl+N + center (user), tests green (agent). Archive on the user's word per house rule.
