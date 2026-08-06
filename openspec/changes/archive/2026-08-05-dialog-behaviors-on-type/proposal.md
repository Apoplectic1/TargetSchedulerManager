# Proposal: dialog-behaviors-on-type

## Why

The app's dialog behaviors (draggable, Ctrl+N diagnostics, lone-button centering) are split between
the `ShowDialogAsync` funnel and the `AppDialog` type, enforced only by comment ("every dialog shows
through here") — and the audit that prompted this (2026-08-05) found the comment already false: the
self-update prompt calls `ShowAsync` directly and silently lacks drag + Ctrl+N. Convention-by-comment
invites exactly this drift; the conventions doc's own rule is invariants-at-the-enforcement-point.

## What Changes

- Every per-dialog behavior moves onto the `AppDialog` type: DragMove attach (constructor), Ctrl+N
  diagnostics hook (PreviewKeyDown, via a static hook the window sets once), lone-button centering
  (already there). Behaviors become true by construction for every `AppDialog`, including the update
  prompt.
- `ShowDialogAsync` shrinks to a thin await seam typed `AppDialog` — the compiler now rejects a raw
  `ContentDialog`, closing the silent-skip hole.
- **Behavior change:** the self-update prompt gains drag + Ctrl+N (it was the one bypass).

## Capabilities

### New Capabilities

_None._

### Modified Capabilities

- `self-update`: the update prompt is a standard app dialog — movable, centered, diagnostics-capable —
  not a bare `ContentDialog` (the "checks for updates at startup" requirement's prompt gains the
  standard dialog surface).

## Impact

- `TargetSchedulerManager.App/Controls/AppDialog.cs` — gains DragMove + Ctrl+N hook.
- `TargetSchedulerManager.App/MainWindow.Dialogs.cs` — funnel thins; signature narrows to `AppDialog`.
- `TargetSchedulerManager.App/MainWindow.Flyouts.cs`, `Services/UpdateService.cs` — construction sites
  already typed `AppDialog`; UpdateService inherits the behaviors with no site change.
- `UI.md` dialog conventions update in the same commit.
- No Library change; no data change.
