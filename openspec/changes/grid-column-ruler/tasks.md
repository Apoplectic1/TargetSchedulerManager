# grid-column-ruler — tasks

## 1. The ruler

- [x] 1.1 `GridColumns.cs` (app root namespace, design D1–D3): the named `(name, width)` ruler table +
      `ApplyRuler` attached property stamping `ColumnDefinitions` in its change callback.
- [x] 1.2 `MainWindow.xaml`: replace the four `ColumnDefinitions` blocks (Group ~31 / Filter ~105 /
      Panel ~200 / header ~358) with `local:GridColumns.ApplyRuler="True"`; update the header's
      "mirrors the row template's column widths" comment to point at the ruler.

## 2. Verify + docs

- [x] 2.1 Build + full test run (regression floor; the ruler itself is render-only).
- [x] 2.2 DOMAIN.md "add a UI element" checklist: column widths/order now edit `GridColumns` once;
      CHANGELOG + ROADMAP digest line. Same commit.
- [ ] 2.3 Human verification pass (user-run, GATES archive — XAML rendering has no test net): columns
      align exactly as before across header / group / filter / panel / expanded detail rows; Target
      star-column absorbs a window resize; hover glyph, inline Desired box, and the Hours pill render
      and behave unchanged.
