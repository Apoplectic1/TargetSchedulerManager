## 1. Push exposes what it applied (TsSync / PushResult)

- [x] 1.1 Extend `PushResult` with the applied collapsed entry set (all `TsEditKind`s, with
      `Table`/`Key`/`Column`/`Value`/`Old`/`RowGuid`) and the push commit timestamp (UNIX seconds
      UTC, stamped at `Journal.CommitPush`) — populated only when entries applied; empty on
      refusal/no-op outcomes
- [x] 1.2 Exclude wholly, at row grain `(Table, Key)`, any row with a failed/retained entry from
      the exposed applied set (its journal entries survive; the next successful push emits it)
- [x] 1.3 `TsSyncTests`: applied-set contents across outcomes — full push, partial push
      (failed-row exclusion), refusal (empty), insert entries carrying `RowGuid`

## 2. Exporter pure mapping (`Services\CatalogInboxExporter`)

- [x] 2.1 Op record model + JSON line serialization for the four v1 ops with the envelope
      (`v: 1`, `at`, `source: "TSM"`), field names and types exactly per the inbox contract
      (`epoch` as string, nullable fields null not omitted-vs-defaulted per contract shapes)
- [x] 2.2 Row resolution: applied entries → affected rows (target guid / plan/template local id /
      insert `RowGuid`) → full row values read from the local working copy post-push (journal says
      *which*, local db says *what*)
- [x] 2.3 Mapping rules: origin filter (`Manual` + `Insert` emit, `WriteBack` never); one
      full-value op per affected row per push; table→op mapping (Target→`target-upsert`,
      ExposurePlan→`exposure-plan-upsert`, Project→`project-upsert`); adoption insert group →
      target + plan(s) upserts (+ project when touched)
- [x] 2.4 Template mirror rule: every `exposure-plan-upsert` is accompanied by the referenced
      template's `exposure-template-upsert` (TS-authored values from the local copy);
      references-first output ordering (project → template → target → plan)
- [x] 2.5 Desired sourcing rule: when a plan row's applied set includes a `WriteBack` `desired`
      entry, source `desired_count` from the `Manual` desired entry when present, else the
      write-back entry's pre-push `Old` — never the ratcheted row value

## 3. File writer + push hook

- [x] 3.1 Inbox file writer: create the contract-named inbox directory if missing; write all lines
      to `tsm-<yyyyMMdd-HHmmss>.jsonl.partial` (UTF-8 no BOM, `\n` endings), flush, close, rename
      to `.jsonl` (atomic publish; never touches other files, including `*.processing`)
- [x] 3.2 Hook in `MainViewModel.PushAsync`: after `Push` returns with entries applied (full or
      partial outcome), run the exporter with the applied set + commit timestamp; skip when
      nothing applied
- [x] 3.3 Failure path (rule #16): any export fault aborts remaining export work, `Log.Error`
      naming the inbox path and failed stage/op, push status line gains a loud
      `— CATALOG EXPORT FAILED: see tsm.log` suffix (push outcome text preserved); journal is
      never retained on export failure

## 4. Tests (file-level contract fixtures — never `Catalog.db`)

- [x] 4.1 Mapping tests: op selection per table, full-value collapse of multi-entry rows, origin
      filter (write-back-only push emits nothing — the pushed-ratchet-silent scenario), co-edit
      desired sourcing (Manual-present and pre-push-Old branches), mirror accompaniment +
      unconditional repeat, adoption three-record set, reference ordering, envelope fields
- [x] 4.2 Writer tests in temp-dir inboxes (`SyncTestEnv.NewDir` style): directory creation,
      naming, no-BOM UTF-8 + `\n`, `.partial`→`.jsonl` publish (no `.partial` visible after
      success), pre-existing `*.jsonl`/`*.processing` files untouched
- [x] 4.3 Hook tests: no emission on refused push or empty applied set; export failure surfaces
      (log + status) without disturbing push outcome or journal state
- [x] 4.4 Full suite green (`dotnet test`); confirm no test references `Catalog.db`

## 5. Docs (same commit as the code)

- [x] 5.1 SUBSYSTEMS.md: add the catalog-export step to the TS sync model narrative (push commit →
      origin-filtered emission → atomic publish; failure = loud suffix, never journal retention),
      pairing with `openspec/specs/catalog-export/`
- [x] 5.2 CLAUDE.md router + ARCHITECTURE.md: one-line duty statement (TSM's one ISM-era duty,
      writer side of the inbox contract, dies at TS retirement); ROADMAP.md status update
