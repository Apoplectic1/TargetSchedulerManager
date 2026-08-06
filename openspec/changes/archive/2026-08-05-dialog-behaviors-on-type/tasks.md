# Tasks: dialog-behaviors-on-type

## 1. Behaviors onto the type

- [x] 1.1 `AppDialog` constructor: attach DragMove; wire PreviewKeyDown to the static
      `DiagnosticsHook` (Ctrl+N, null-safe); doc comment states the type-carries-behavior rule
- [x] 1.2 `MainWindow` sets `AppDialog.DiagnosticsHook` once at construction

## 2. Thin the funnel

- [x] 2.1 `ShowDialogAsync` signature narrows to `Controls.AppDialog`; DragMove + PreviewKeyDown move
      out; retype the 7 in-window construction-site variables so calls compile without casts
- [x] 2.2 Build confirms no raw `ContentDialog` reaches the funnel anywhere

## 3. Docs + verify

- [x] 3.1 `UI.md`: dialog-conventions bullets say behaviors ride the `AppDialog` type (funnel = await
      seam only; update prompt no longer an exception) — same commit as the code
- [x] 3.2 Build + app tests green; user verifies editor dialogs still drag/center/Ctrl+N; archive on
      their word
