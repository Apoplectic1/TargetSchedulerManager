# Tasks: harden-ts-pull

## 1. Atomic pull (D1)

- [x] 1.1 `TsSync.Pull`: back up into `<local>.pull-tmp`, `ClearAllPools()`, atomic swap over the local db,
      then record baseline — pre-swap inbound snapshot ordering preserved; stale `pull-tmp*` (incl. SQLite
      sidecars) swept before each pull
- [x] 1.2 Tests: swap semantics (success replaces, old db intact until swap), stale-tmp sweep, kill-window
      simulation (tmp left behind ⇒ next pull sweeps; post-swap/pre-baseline ⇒ mismatch pulls again),
      pooled-reader-open-during-swap does not fail the swap

## 2. Torn-local gate + heal (D2)

- [x] 2.1 Torn-state check in the load path before any local read and before the baseline skip decision:
      `-journal`/`-wal` beside the local db ⇒ `Log.Error`, discard db + sidecars + baseline, force pull;
      BIRDWATCHER unreachable ⇒ loud load failure naming the torn file; `.tsm-edits.jsonl` untouched
- [x] 2.2 Tests: heal matrix — hot journal ⇒ heal + pull; torn + offline ⇒ loud fail, no read, no delete of
      edit journal; torn + dirty journal ⇒ edits survive heal and remain pushable; healthy local ⇒ gate is
      a no-op and skip rule still applies

## 3. Chunked backup with percentage + cancel (D3)

- [x] 3.1 Replace `BackupDatabase` with a SQLitePCL.raw `backup_init`/`backup_step` loop (~512 pages/step)
      in `TsSync`, reporting percent via `IProgress<int>` and honoring a `CancellationToken` between steps
      (busy/locked steps retried within the existing 2 s patience); cancel ⇒ dispose + delete tmp, no
      baseline, previous local db untouched
- [x] 3.2 Thread progress/cancel through `PullIfChanged`/`Pull` callers (`PrepareTsForLoadAsync`, push's
      closing pull, Pull Now)
- [x] 3.3 UI: status text shows `pulling from BIRDWATCHER … NN%` (text percentage only — **no ProgressBar
      element**); cancel affordance visible only while a pull is in flight (DOMAIN.md add-a-UI-element
      checklist); cancelled first-ever pull fails the load loudly
- [x] 3.4 Tests: progress monotonic 0→100 against a real temp db; cancel mid-copy leaves old db + no
      baseline + tmp gone; cancelled first-ever pull surfaces the loud failure

## 4. Pull logging (D4)

- [x] 4.1 Log lines: `PULL starting (<bytes> from <remote>)`, completion duration, `PULL cancelled at NN% —
      tmp discarded`, and the heal gate's `LOCAL TORN` error; verify each appears in the flows' tests

## 5. Verify + docs (same commit as code)

- [ ] 5.1 Full build + both test projects green; manual pass per VERIFICATION.md (user verifies: percentage
      text during a real pull, cancel behavior, heal on a fabricated hot journal)
- [x] 5.2 `ARCHITECTURE.md` sync-model section: atomic pull, torn-local gate, observability; mirror the
      condensed invariant in `CLAUDE.md` if the sync bullet's wording changes
- [x] 5.3 `ROADMAP.md`: recently-shipped entry; mark the parked "differ-RW hot-journal hardening" follow-up
      superseded by this change
