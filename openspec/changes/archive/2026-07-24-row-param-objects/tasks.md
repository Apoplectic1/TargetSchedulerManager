# row-param-objects — tasks

## 1. Records + constructor

- [x] 1.1 `ReconciliationRow.cs`: add `RowIdentity` + `RowNumbers` records (design D1); constructor →
      12 params (D2); every property initializer re-points to the records — names/types unchanged,
      `Apply*` methods verbatim.

## 2. Call sites (named args on every `RowNumbers`)

- [x] 2.1 `ReconciliationLoader.EmitRows`: build `RowIdentity` once at the top; rewrite the Both-rollup
      inline site, `BothRow`/`TsRow`/`DiskRow`, and the no-data inline site (D3/D4).
- [x] 2.2 `Make.Leaf`: same keyword surface, body constructs the records — no test-body changes.

## 3. Verify + docs + archive

- [x] 3.1 Build + full test run (slnx-only) — `BuildRowsTests` + `RowTests` are the transposition lock.
- [x] 3.2 CHANGELOG entry + ROADMAP digest line, same commit.
- [x] 3.3 Auto-archive (pure refactor, standing rule 2026-07-24) — sync the in-place-mirror delta (no
      requirement-level change).
