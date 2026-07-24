# Guarded TS Write — `TsSource` + `TsEditGate` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Pull the LIVE/LOCAL state machine and the guarded TS write — today smeared across `MainViewModel`, `TsDatabaseResolver`, and the library editor — into two deep, injected, unit-tested app modules (`TsSource` + `TsEditGate`) over one consumer-neutral library guarded-apply (`TargetSchedulerEditor.TrySetField`).

**Architecture:** `TsSource` (app) owns the two db paths, the injected liveness probe, and the Live/Local + sticky state; it's consulted by both the load (for the path) and the gate. `TsEditGate` (app) holds a `TsSource` + an injected editor factory and exposes one `ApplyAsync(...) → EditOutcome`; on a live-drop fault it delegates the sticky-fall to `TsSource`. The library's `TargetSchedulerEditor` gains a `TrySetField` that folds its four guard predicates into one call returning a structured `RefusalReason`. `MainViewModel` loses its probe block and its ~60-line `ApplyFieldEditAsync`, calling the gate instead.

**Tech Stack:** .NET 10, WinUI 3 (`tsmui`), `Microsoft.Data.Sqlite`, `Astronomy.Catalog` (cross-repo `ProjectReference`), `Astronomy.Diagnostics` (`Log`), xUnit v3.

## Global Constraints

- **Two repos.** Library code lives in a **separate git repo** at `E:\Projects\VisualStudio\Astronomy\Library`; its changes commit there (Task 1), separately from the TSM repo (Tasks 2–4). Use `git -C <path>` so the working directory is never ambiguous.
- **Shared-library neutrality.** `Astronomy.Catalog` is consumed by XFM/TP/IS — the new surface uses a **structured `RefusalReason` enum, never UI strings**; user wording is mapped app-side. No consumer terminology in library doc-comments.
- **No migration / back-compat code.** Single consumer, clean rebuild assumed. Refusal *guards* are fine; upgraders are not.
- **Fail-fast, surfaced.** A guarded refusal is *loud* (revert + status + audit), never a silent fallback. The result-type serves this — it does not violate it.
- **Docs commit with code.** `ARCHITECTURE.md` (Task 4) and the Library doc line (Task 1) land in the **same commit** as their code.
- **Build is pure-managed** — plain `dotnet build`/`dotnet test` (no `.vcxproj` in this graph; the VS-MSBuild caveat does not apply).
- **xUnit v3 trap.** `xunit.v3` lives only in `TargetSchedulerManager.App.Tests` — never let a "Manage NuGet for Solution → all projects" action spray it onto `TargetSchedulerManager.App`.
- **Build/test commands:**
  - TSM build: `dotnet build TargetSchedulerManager.slnx -v:m -nologo`
  - TSM tests: `dotnet test TargetSchedulerManager.slnx -v:q --nologo` (add `--filter "FullyQualifiedName~<Class>"` to scope)
  - Library tests: `dotnet test "E:\Projects\VisualStudio\Astronomy\Library\Astronomy.Catalog.Tests\Astronomy.Catalog.Tests.csproj" --filter "FullyQualifiedName~TargetSchedulerEditorTests"`

---

## File Structure

| File | Repo | Responsibility |
|---|---|---|
| `Astronomy.Catalog/TargetScheduler/TargetSchedulerEditor.cs` | Library | **Modify** — add `RefusalReason` enum + `TrySetField` (folds the 4 predicates). |
| `Astronomy.Catalog.Tests/TargetScheduler/TargetSchedulerEditorTests.cs` | Library | **Modify** — add 5 `TrySetField` cases. |
| `..\Library\CLAUDE.md` (or the editor's doc block) | Library | **Modify** — one consumer-neutral line on the guarded-apply. |
| `TargetSchedulerManager.App/Shared/TsSource.cs` | TSM | **Create** — `TsMode` enum + `TsSource` (paths · probe · mode/sticky). |
| `TargetSchedulerManager.App/Shared/TsEditGate.cs` | TSM | **Create** — `ITsEditor`, `TsEditorAdapter`, `EditOutcome`, `TsEditGate`. |
| `TargetSchedulerManager.App/ViewModels/MainViewModel.cs` | TSM | **Modify** — hold the gate; rewire load + edits; delete `ApplyFieldEditAsync`; drop `_tsMode/_liveDisabled/_tsProbed/_tsDbPath`; remove the local `TsMode`. |
| `TargetSchedulerManager.App/MainWindow.xaml.cs` | TSM | **Modify** — add `using TargetSchedulerManager.App.Shared;` (relocated `TsMode`). |
| `TargetSchedulerManager.App.Tests/TsSourceTests.cs` | TSM | **Create** — 6 state-machine cases. |
| `TargetSchedulerManager.App.Tests/TsEditGateTests.cs` | TSM | **Create** — 5 gate cases (stub `ITsEditor`). |
| `TargetSchedulerManager.App.Tests/Make.cs` | TSM | **Modify** — add `planTsKey` to `Leaf`. |
| `TargetSchedulerManager.App.Tests/MainViewModelTests.cs` | TSM | **Modify** — 1 gate-wired in-place-apply case. |
| `ARCHITECTURE.md` | TSM | **Modify** — add `TsSource`/`TsEditGate` + the library `TrySetField`. |

`TsDatabaseResolver.IsLiveReachable` stays — it becomes the **default injected probe** (the real adapter). New `.cs` files auto-include (SDK-style); `InternalsVisibleTo("TargetSchedulerManager.App.Tests")` is already set in the App csproj, so internal `TsSource`/`TsEditGate`/`ITsEditor`/`EditOutcome` are test-visible.

---

## Task 1: Library guarded-apply — `RefusalReason` + `TrySetField`

**Files:**
- Modify: `E:\Projects\VisualStudio\Astronomy\Library\Astronomy.Catalog\TargetScheduler\TargetSchedulerEditor.cs`
- Test: `E:\Projects\VisualStudio\Astronomy\Library\Astronomy.Catalog.Tests\TargetScheduler\TargetSchedulerEditorTests.cs`
- Doc: `E:\Projects\VisualStudio\Astronomy\Library\CLAUDE.md`

**Interfaces:**
- Produces: `enum RefusalReason { None, SchemaIncompatible, ReadOnly, OpenSidecar, ColumnAbsent }` and `(FieldEditResult? Result, RefusalReason Refusal) TargetSchedulerEditor.TrySetField(TsTable table, string tsKey, string column, object? value)`.
- Consumes: the editor's existing `HasRequiredColumns`, `IsReadOnly`, `HasOpenSidecar`, `IsFieldAvailable(table,column)`, `SetField(...)`, and `FieldEditResult`.

- [ ] **Step 1: Write the failing tests** — append to `TargetSchedulerEditorTests.cs` (reuses the file's existing `NewFullDb()`, `ReadScalar`, `TestSupport.NewDbPath/Cleanup`):

```csharp
[Fact]
public void TrySetField_CleanDb_AppliesAndReturnsNone()
{
    string db = NewFullDb();
    try
    {
        using TargetSchedulerEditor editor = new(db);
        (FieldEditResult? result, RefusalReason refusal) = editor.TrySetField(TsTable.ExposurePlan, "ep-1", "desired", 140);
        Assert.Equal(RefusalReason.None, refusal);
        Assert.True(result!.Succeeded);
        Assert.Equal(140L, ReadScalar(db, "SELECT desired FROM exposureplan WHERE guid='ep-1'"));
    }
    finally { TestSupport.Cleanup(db); }
}

[Fact]
public void TrySetField_OpenSidecar_RefusesAndWritesNothing()
{
    string db = NewFullDb();
    File.WriteAllText(db + "-wal", "");   // a stray WAL sidecar ⇒ db may be open elsewhere (TS mid-transaction)
    try
    {
        using TargetSchedulerEditor editor = new(db);
        (FieldEditResult? result, RefusalReason refusal) = editor.TrySetField(TsTable.ExposurePlan, "ep-1", "desired", 140);
        Assert.Equal(RefusalReason.OpenSidecar, refusal);
        Assert.Null(result);
        Assert.Equal(10L, ReadScalar(db, "SELECT desired FROM exposureplan WHERE guid='ep-1'"));   // untouched
    }
    finally { File.Delete(db + "-wal"); TestSupport.Cleanup(db); }
}

[Fact]
public void TrySetField_ReadOnlyFile_Refuses()
{
    string db = NewFullDb();
    File.SetAttributes(db, FileAttributes.ReadOnly);
    try
    {
        using TargetSchedulerEditor editor = new(db);
        (FieldEditResult? result, RefusalReason refusal) = editor.TrySetField(TsTable.ExposurePlan, "ep-1", "desired", 140);
        Assert.Equal(RefusalReason.ReadOnly, refusal);
        Assert.Null(result);
    }
    finally { File.SetAttributes(db, FileAttributes.Normal); TestSupport.Cleanup(db); }
}

[Fact]
public void TrySetField_MissingColumn_RefusesColumnAbsent()
{
    string db = NewFullDb();   // project has no `filterswitchfrequency` column (see IsFieldAvailable_* test)
    try
    {
        using TargetSchedulerEditor editor = new(db);
        (FieldEditResult? result, RefusalReason refusal) = editor.TrySetField(TsTable.Project, "pr-1", "filterswitchfrequency", 2);
        Assert.Equal(RefusalReason.ColumnAbsent, refusal);
        Assert.Null(result);
    }
    finally { TestSupport.Cleanup(db); }
}

[Fact]
public void TrySetField_TargetLacksActive_RefusesSchemaIncompatible()
{
    string db = TestSupport.NewDbPath();
    using (SqliteConnection setup = new(new SqliteConnectionStringBuilder { DataSource = db }.ToString()))
    {
        setup.Open();
        using SqliteCommand cmd = setup.CreateCommand();
        cmd.CommandText = "CREATE TABLE target (Id INTEGER PRIMARY KEY, guid TEXT, name TEXT);";   // no `active`
        cmd.ExecuteNonQuery();
    }
    SqliteConnection.ClearAllPools();
    try
    {
        using TargetSchedulerEditor editor = new(db);
        (FieldEditResult? result, RefusalReason refusal) = editor.TrySetField(TsTable.Target, "g-1", "priority", 1);
        Assert.Equal(RefusalReason.SchemaIncompatible, refusal);
        Assert.Null(result);
    }
    finally { TestSupport.Cleanup(db); }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test "E:\Projects\VisualStudio\Astronomy\Library\Astronomy.Catalog.Tests\Astronomy.Catalog.Tests.csproj" --filter "FullyQualifiedName~TrySetField"`
Expected: FAIL — compile error, `TargetSchedulerEditor` has no `TrySetField` / `RefusalReason` not found.

- [ ] **Step 3: Add `RefusalReason` + `TrySetField`** to `TargetSchedulerEditor.cs`. Add the enum just above the `TargetSchedulerEditor` class declaration:

```csharp
/// <summary>Why a guarded field write was refused — a structured reason the consumer maps to its own wording
/// (this library names no consumer). <see cref="None"/> means the write proceeded.</summary>
public enum RefusalReason { None, SchemaIncompatible, ReadOnly, OpenSidecar, ColumnAbsent }
```

Add this method inside the class, immediately after `SetField` (around line 137):

```csharp
/// <summary>
/// The guarded entry point: checks the open db's safety predicates in order — required columns present, file
/// writable, no open <c>-wal</c>/<c>-shm</c>/<c>-journal</c> sidecar, and the target column actually present on
/// this db version — and, only if all pass, performs the read-back-verified <see cref="SetField"/>. Returns the
/// edit result with <see cref="RefusalReason.None"/>, or a null result with the structured reason it refused. The
/// caller owns the user-facing wording; this collapses the four predicates a consumer would otherwise re-assemble.
/// </summary>
public (FieldEditResult? Result, RefusalReason Refusal) TrySetField(TsTable table, string tsKey, string column, object? value)
{
    if (!HasRequiredColumns) return (null, RefusalReason.SchemaIncompatible);
    if (IsReadOnly) return (null, RefusalReason.ReadOnly);
    if (HasOpenSidecar) return (null, RefusalReason.OpenSidecar);
    if (!IsFieldAvailable(table, column)) return (null, RefusalReason.ColumnAbsent);
    return (SetField(table, tsKey, column, value), RefusalReason.None);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test "E:\Projects\VisualStudio\Astronomy\Library\Astronomy.Catalog.Tests\Astronomy.Catalog.Tests.csproj" --filter "FullyQualifiedName~TrySetField"`
Expected: PASS — 5/5.

- [ ] **Step 5: Update the Library doc** — add one line under the TS-interop notes in `..\Library\CLAUDE.md` (consumer-neutral):

```markdown
- `TargetSchedulerEditor.TrySetField(table, key, column, value) → (FieldEditResult?, RefusalReason)` is the guarded
  entry point: it folds the four open-db predicates (required columns / read-only / open sidecar / column present)
  into one structured-refusal call. Consumers map `RefusalReason` to their own wording — the library names none.
```

- [ ] **Step 6: Commit (Library repo)**

```bash
git -C "E:/Projects/VisualStudio/Astronomy/Library" add Astronomy.Catalog/TargetScheduler/TargetSchedulerEditor.cs Astronomy.Catalog.Tests/TargetScheduler/TargetSchedulerEditorTests.cs CLAUDE.md
git -C "E:/Projects/VisualStudio/Astronomy/Library" commit -m "feat(catalog): guarded TrySetField + structured RefusalReason on TargetSchedulerEditor"
```

---

## Task 2: `TsSource` (paths + liveness + mode), relocate `TsMode`

**Files:**
- Create: `TargetSchedulerManager.App/Shared/TsSource.cs`
- Modify: `TargetSchedulerManager.App/ViewModels/MainViewModel.cs` (remove the `public enum TsMode {...}` declaration only — leave the rest for Task 4)
- Modify: `TargetSchedulerManager.App/MainWindow.xaml.cs` (add `using TargetSchedulerManager.App.Shared;`)
- Test: `TargetSchedulerManager.App.Tests/TsSourceTests.cs`

**Interfaces:**
- Produces: `enum TsMode { Live, Local }`; `internal sealed class TsSource` with ctor `TsSource(string livePath, string localPath, Func<bool> probe)`, `string CurrentPath`, `bool IsLive`, `bool LiveEnabled`, `string ResolvePathForLoad()`, `bool TrySelectMode(TsMode mode)`, `bool NotifyLiveWriteFailed()`, and `static TsSource CreateDefault()`.
- Consumes: `DevDefaults.TsDatabaseLive` / `DevDefaults.TsDatabase`, `TsDatabaseResolver.IsLiveReachable` (both visible via the enclosing `TargetSchedulerManager` namespace).

- [ ] **Step 1: Write the failing tests** — `TsSourceTests.cs`:

```csharp
using TargetSchedulerManager.App.Shared;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

// The LIVE/LOCAL state machine in isolation, with the liveness probe injected (no SMB, no DevDefaults paths).
public class TsSourceTests
{
    private static TsSource New(Func<bool> probe) => new("LIVE", "LOCAL", probe);

    [Fact]
    public void FirstResolve_ProbeReachable_SelectsLive()
    {
        TsSource s = New(() => true);
        Assert.Equal("LIVE", s.ResolvePathForLoad());
        Assert.True(s.IsLive);
        Assert.True(s.LiveEnabled);
    }

    [Fact]
    public void FirstResolve_ProbeDown_FallsToLocal_AndDisablesLive()
    {
        TsSource s = New(() => false);
        Assert.Equal("LOCAL", s.ResolvePathForLoad());
        Assert.False(s.IsLive);
        Assert.False(s.LiveEnabled);   // sticky-disabled
    }

    [Fact]
    public void SecondResolve_WasLive_ProbeNowDown_FallsToLocal()
    {
        bool reachable = true;
        TsSource s = New(() => reachable);
        s.ResolvePathForLoad();                 // → Live
        reachable = false;
        Assert.Equal("LOCAL", s.ResolvePathForLoad());
        Assert.False(s.IsLive);
        Assert.False(s.LiveEnabled);
    }

    [Fact]
    public void TrySelectMode_LiveWhenStickyDisabled_IsIgnored()
    {
        TsSource s = New(() => false);
        s.ResolvePathForLoad();                 // Local + LiveEnabled false
        Assert.False(s.TrySelectMode(TsMode.Live));
        Assert.False(s.IsLive);
    }

    [Fact]
    public void NotifyLiveWriteFailed_LiveAndNowUnreachable_DropsToLocal()
    {
        bool reachable = true;
        TsSource s = New(() => reachable);
        s.ResolvePathForLoad();                 // → Live
        reachable = false;
        Assert.True(s.NotifyLiveWriteFailed());
        Assert.False(s.IsLive);
        Assert.False(s.LiveEnabled);
    }

    [Fact]
    public void NotifyLiveWriteFailed_LiveButStillReachable_NotADrop()
    {
        TsSource s = New(() => true);
        s.ResolvePathForLoad();                 // → Live
        Assert.False(s.NotifyLiveWriteFailed()); // some other fault, not a drop
        Assert.True(s.IsLive);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test TargetSchedulerManager.slnx --nologo --filter "FullyQualifiedName~TsSourceTests"`
Expected: FAIL — `TsSource` / `TsMode` not found.

- [ ] **Step 3: Create `TsSource.cs`**:

```csharp
namespace TargetSchedulerManager.App.Shared;

/// <summary>Which TS database this session reads + edits — the user's LIVE/LOCAL choice.</summary>
public enum TsMode { Live, Local }

/// <summary>
/// Owns the TS-source policy for one app session: the LIVE (BIRDWATCHER, over SMB) and LOCAL (working-copy) paths,
/// the injected reachability <paramref name="probe"/>, and the Live/Local + sticky-disabled state. Consulted by
/// both the load (via <see cref="ResolvePathForLoad"/>) and the write gate (via <see cref="CurrentPath"/> and
/// <see cref="NotifyLiveWriteFailed"/>). UI-free: it exposes state, never raises change notifications — the
/// view-model refreshes its bindings after each (awaited) call. Machine/network policy, so it lives in the App's
/// <c>Shared\</c> folder, never the consumer-neutral library.
/// </summary>
internal sealed class TsSource
{
    private readonly string _livePath;
    private readonly string _localPath;
    private readonly Func<bool> _probe;
    private TsMode _mode = TsMode.Local;
    private bool _liveDisabled;
    private bool _probed;

    public TsSource(string livePath, string localPath, Func<bool> probe)
    {
        _livePath = livePath;
        _localPath = localPath;
        _probe = probe;
    }

    /// <summary>The real session: the live BIRDWATCHER db, the restorable local copy, and the SMB reachability probe.</summary>
    public static TsSource CreateDefault() =>
        new(DevDefaults.TsDatabaseLive, DevDefaults.TsDatabase, TsDatabaseResolver.IsLiveReachable);

    /// <summary>The db the load and the gate currently act on.</summary>
    public string CurrentPath => _mode == TsMode.Live ? _livePath : _localPath;

    /// <summary>True when this session is on the LIVE BIRDWATCHER db.</summary>
    public bool IsLive => _mode == TsMode.Live;

    /// <summary>False once a probe found BIRDWATCHER unreachable this session (the LIVE choice greys out — sticky).</summary>
    public bool LiveEnabled => !_liveDisabled;

    /// <summary>
    /// Resolves the path to scan/read this load: the first call probes (LIVE if reachable, else LOCAL + LIVE
    /// sticky-disabled); thereafter a LIVE load re-probes and sticky-falls to LOCAL if the rig dropped.
    /// </summary>
    public string ResolvePathForLoad()
    {
        if (!_probed)
        {
            _probed = true;
            bool reachable = _probe();
            _liveDisabled = !reachable;
            _mode = reachable ? TsMode.Live : TsMode.Local;
        }
        else if (_mode == TsMode.Live && !_probe())
        {
            _liveDisabled = true;
            _mode = TsMode.Local;
        }
        return CurrentPath;
    }

    /// <summary>Honours a LIVE/LOCAL radio choice; a sticky-disabled LIVE is ignored. Returns true when the mode
    /// actually changed (the caller then reloads).</summary>
    public bool TrySelectMode(TsMode mode)
    {
        if (mode == TsMode.Live && _liveDisabled) return false;
        if (mode == _mode) return false;
        _mode = mode;
        return true;
    }

    /// <summary>The gate calls this after a write fault: if we are LIVE and a re-probe now finds BIRDWATCHER
    /// unreachable, sticky-fall to LOCAL and return true (it was a live drop). Otherwise return false (some other
    /// fault — the gate reports a failure instead).</summary>
    public bool NotifyLiveWriteFailed()
    {
        if (_mode != TsMode.Live) return false;
        if (_probe()) return false;
        _liveDisabled = true;
        _mode = TsMode.Local;
        return true;
    }
}
```

- [ ] **Step 4: Remove the old `TsMode` from `MainViewModel.cs`.** Delete these lines (≈24–25):

```csharp
/// <summary>Which TS database this session reads + edits — the user's LIVE/LOCAL radio choice.</summary>
public enum TsMode { Live, Local }
```

`MainViewModel` already has `using TargetSchedulerManager.App.Shared;`, so its remaining `TsMode` references still resolve. In `MainWindow.xaml.cs`, add the import near the top:

```csharp
using TargetSchedulerManager.App.Shared;
```

- [ ] **Step 5: Run the tests to verify they pass and nothing regressed**

Run: `dotnet build TargetSchedulerManager.slnx -v:m -nologo`
Expected: Build succeeded, 0 errors (the relocated `TsMode` resolves in the VM and code-behind).

Run: `dotnet test TargetSchedulerManager.slnx -v:q --nologo --filter "FullyQualifiedName~TsSourceTests"`
Expected: PASS — 6/6.

- [ ] **Step 6: Commit**

```bash
git add TargetSchedulerManager.App/Shared/TsSource.cs TargetSchedulerManager.App/ViewModels/MainViewModel.cs TargetSchedulerManager.App/MainWindow.xaml.cs TargetSchedulerManager.App.Tests/TsSourceTests.cs
git commit -m "feat: TsSource owns LIVE/LOCAL path + liveness state (probe injected, unit-tested)"
```

---

## Task 3: `TsEditGate` (guarded write) + `ITsEditor` port + `EditOutcome`

**Files:**
- Create: `TargetSchedulerManager.App/Shared/TsEditGate.cs`
- Test: `TargetSchedulerManager.App.Tests/TsEditGateTests.cs`

**Interfaces:**
- Consumes: `TsSource` (Task 2); library `TsTable`, `RefusalReason`, `FieldEditResult`, `TargetSchedulerEditor` (Task 1); `Astronomy.Diagnostics.Log`; `Microsoft.Data.Sqlite.SqliteConnection`.
- Produces:
  - `internal interface ITsEditor : IDisposable { (FieldEditResult? Result, RefusalReason Refusal) TrySetField(TsTable table, string tsKey, string column, object? value); }`
  - `internal abstract record EditOutcome` with `Applied(string? Old, object? New)`, `Refused(RefusalReason Reason)`, `Failed(bool Found, bool Verified)`, `LiveDropped`.
  - `internal sealed class TsEditGate` with ctor `TsEditGate(TsSource source, Func<string, ITsEditor> editorFactory)`, `TsSource Source`, `Task<EditOutcome> ApplyAsync(TsTable table, string key, string column, object? value, string label)`, and `static TsEditGate CreateDefault()`.

- [ ] **Step 1: Write the failing tests** — `TsEditGateTests.cs`:

```csharp
using Astronomy.Catalog.TargetScheduler;
using TargetSchedulerManager.App.Shared;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

// The guarded write in isolation: a stub ITsEditor (no SQLite) + a TsSource with an injected probe.
public class TsEditGateTests
{
    private sealed class StubEditor : ITsEditor
    {
        public (FieldEditResult? Result, RefusalReason Refusal) Next = (null, RefusalReason.None);
        public bool Throw;
        public (FieldEditResult? Result, RefusalReason Refusal) TrySetField(
            TsTable table, string tsKey, string column, object? value) =>
            Throw ? throw new InvalidOperationException("boom") : Next;
        public void Dispose() { }
    }

    private static TsSource Live() { TsSource s = new("LIVE", "LOCAL", () => true); s.ResolvePathForLoad(); return s; }

    [Fact]
    public async Task CleanWrite_ReturnsApplied()
    {
        StubEditor ed = new() { Next = (new FieldEditResult(RowFound: true, OldValue: "5", Verified: true), RefusalReason.None) };
        TsEditGate gate = new(Live(), _ => ed);
        EditOutcome o = await gate.ApplyAsync(TsTable.ExposurePlan, "ep-1", "desired", 10, "A · H");
        EditOutcome.Applied a = Assert.IsType<EditOutcome.Applied>(o);
        Assert.Equal("5", a.Old);
        Assert.Equal(10, a.New);
    }

    [Fact]
    public async Task RefusedWrite_PassesTheReasonThrough()
    {
        StubEditor ed = new() { Next = (null, RefusalReason.OpenSidecar) };
        TsEditGate gate = new(Live(), _ => ed);
        EditOutcome o = await gate.ApplyAsync(TsTable.ExposurePlan, "ep-1", "desired", 10, "A · H");
        Assert.Equal(RefusalReason.OpenSidecar, Assert.IsType<EditOutcome.Refused>(o).Reason);
    }

    [Fact]
    public async Task VerifyFails_ReturnsFailed()
    {
        StubEditor ed = new() { Next = (new FieldEditResult(RowFound: true, OldValue: "5", Verified: false), RefusalReason.None) };
        TsEditGate gate = new(Live(), _ => ed);
        EditOutcome.Failed f = Assert.IsType<EditOutcome.Failed>(
            await gate.ApplyAsync(TsTable.ExposurePlan, "ep-1", "desired", 10, "A · H"));
        Assert.True(f.Found);
        Assert.False(f.Verified);
    }

    [Fact]
    public async Task EditorThrows_LiveNowUnreachable_ReturnsLiveDropped_AndSourceFalls()
    {
        bool reachable = true;
        TsSource src = new("LIVE", "LOCAL", () => reachable);
        src.ResolvePathForLoad();                       // → Live
        reachable = false;                              // BIRDWATCHER drops mid-write
        StubEditor ed = new() { Throw = true };
        TsEditGate gate = new(src, _ => ed);
        EditOutcome o = await gate.ApplyAsync(TsTable.Target, "g-1", "active", 1, "A");
        Assert.IsType<EditOutcome.LiveDropped>(o);
        Assert.False(src.IsLive);                       // sticky-fell to LOCAL
    }

    [Fact]
    public async Task EditorThrows_NotLive_ReturnsFailed()
    {
        TsSource src = new("LIVE", "LOCAL", () => false);
        src.ResolvePathForLoad();                       // → Local
        StubEditor ed = new() { Throw = true };
        TsEditGate gate = new(src, _ => ed);
        Assert.IsType<EditOutcome.Failed>(await gate.ApplyAsync(TsTable.Target, "g-1", "active", 1, "A"));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test TargetSchedulerManager.slnx --nologo --filter "FullyQualifiedName~TsEditGateTests"`
Expected: FAIL — `ITsEditor` / `EditOutcome` / `TsEditGate` not found.

- [ ] **Step 3: Create `TsEditGate.cs`**:

```csharp
using Astronomy.Catalog.TargetScheduler;
using Astronomy.Diagnostics;
using Microsoft.Data.Sqlite;

namespace TargetSchedulerManager.App.Shared;

/// <summary>The minimal write surface the gate needs from a TS editor — the seam tests stub. The production
/// adapter wraps the library's <see cref="TargetSchedulerEditor"/>.</summary>
internal interface ITsEditor : IDisposable
{
    (FieldEditResult? Result, RefusalReason Refusal) TrySetField(TsTable table, string tsKey, string column, object? value);
}

/// <summary>Production adapter: opens a real <see cref="TargetSchedulerEditor"/> on the given path.</summary>
internal sealed class TsEditorAdapter : ITsEditor
{
    private readonly TargetSchedulerEditor _editor;
    public TsEditorAdapter(string path) => _editor = new TargetSchedulerEditor(path);
    public (FieldEditResult? Result, RefusalReason Refusal) TrySetField(TsTable table, string tsKey, string column, object? value) =>
        _editor.TrySetField(table, tsKey, column, value);
    public void Dispose() => _editor.Dispose();
}

/// <summary>The outcome of one guarded write — a sealed set so callers match exhaustively.</summary>
internal abstract record EditOutcome
{
    private EditOutcome() { }
    /// <summary>The write was applied and read-back verified.</summary>
    public sealed record Applied(string? Old, object? New) : EditOutcome;
    /// <summary>The db was unsafe to write; the value was not changed.</summary>
    public sealed record Refused(RefusalReason Reason) : EditOutcome;
    /// <summary>The row was missing, or the read-back did not confirm the write.</summary>
    public sealed record Failed(bool Found, bool Verified) : EditOutcome;
    /// <summary>A LIVE write threw and a re-probe found BIRDWATCHER gone — the session fell to LOCAL.</summary>
    public sealed record LiveDropped : EditOutcome;
}

/// <summary>
/// The single guarded write path, shared by every TS field edit. Holds a <see cref="TsSource"/> (it writes
/// whichever db is currently selected) and an injected editor factory (the test seam). Runs off the UI thread;
/// on a fault it asks the source to classify a live-drop (sticky-falling to LOCAL) versus an ordinary failure.
/// Drops SQLite's connection pool after a successful write so the next read re-opens the file (an SMB pooled
/// reader can otherwise serve stale pages), and audits the write to the diagnostics log.
/// </summary>
internal sealed class TsEditGate
{
    private readonly TsSource _source;
    private readonly Func<string, ITsEditor> _editorFactory;

    public TsEditGate(TsSource source, Func<string, ITsEditor> editorFactory)
    {
        _source = source;
        _editorFactory = editorFactory;
    }

    /// <summary>The real gate: the default <see cref="TsSource"/> and the production editor adapter.</summary>
    public static TsEditGate CreateDefault() => new(TsSource.CreateDefault(), path => new TsEditorAdapter(path));

    /// <summary>The TS-source policy this gate writes through — the view-model reads it for the load path and the radio bindings.</summary>
    public TsSource Source => _source;

    /// <summary>Guard-checks and applies one field edit to the currently-selected TS db, off the UI thread.</summary>
    public Task<EditOutcome> ApplyAsync(TsTable table, string key, string column, object? value, string label) =>
        Task.Run<EditOutcome>(() =>
        {
            try
            {
                using ITsEditor editor = _editorFactory(_source.CurrentPath);
                (FieldEditResult? result, RefusalReason refusal) = editor.TrySetField(table, key, column, value);
                if (refusal != RefusalReason.None)
                    return new EditOutcome.Refused(refusal);
                if (result is not { Succeeded: true })
                    return new EditOutcome.Failed(result?.RowFound ?? false, result?.Verified ?? false);

                // Over SMB a pooled reader can serve cached pages, making a verified write read as if it hadn't
                // taken — drop the pool so the next read re-opens the file.
                SqliteConnection.ClearAllPools();
                Log.Info($"EDIT {table}.{column} \"{label}\": {result.OldValue} -> {value} on {(_source.IsLive ? "LIVE" : "local")} {_source.CurrentPath}");
                return new EditOutcome.Applied(result.OldValue, value);
            }
            catch (Exception ex)
            {
                if (_source.NotifyLiveWriteFailed())
                    return new EditOutcome.LiveDropped();
                Log.Error($"{table}.{column} write threw for \"{label}\"", ex);
                return new EditOutcome.Failed(Found: false, Verified: false);
            }
        });
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test TargetSchedulerManager.slnx -v:q --nologo --filter "FullyQualifiedName~TsEditGateTests"`
Expected: PASS — 5/5.

- [ ] **Step 5: Commit**

```bash
git add TargetSchedulerManager.App/Shared/TsEditGate.cs TargetSchedulerManager.App.Tests/TsEditGateTests.cs
git commit -m "feat: TsEditGate — one guarded TS write (EditOutcome), editor port injected, unit-tested"
```

---

## Task 4: Rewire `MainViewModel`, delete `ApplyFieldEditAsync`, update docs

**Files:**
- Modify: `TargetSchedulerManager.App/ViewModels/MainViewModel.cs`
- Modify: `TargetSchedulerManager.App.Tests/Make.cs`
- Modify: `TargetSchedulerManager.App.Tests/MainViewModelTests.cs`
- Modify: `ARCHITECTURE.md`

**Interfaces:**
- Consumes: `TsEditGate` (Task 3), `TsSource`/`TsMode` (Task 2), `EditOutcome`, `RefusalReason`.
- Produces: `internal MainViewModel(TsEditGate gate)` test ctor; unchanged public method names `LoadAsync`, `SetTsMode`, `SetTargetEnabledAsync`, `SetPlanDesiredAsync` and binding props `IsLiveSelected`/`IsLocalSelected`/`LiveEnabled`/`TsSourceTooltip`.

- [ ] **Step 1: Write the failing test** — add to `MainViewModelTests.cs` (drives the gate-wired in-place apply):

```csharp
[Fact]
public async Task SetPlanDesired_AppliedWrite_UpdatesLeafAndHeaderInPlace()
{
    var ed = new TsEditGateTests_Stub { Next = (new Astronomy.Catalog.TargetScheduler.FieldEditResult(true, "10", true),
                                                 Astronomy.Catalog.TargetScheduler.RefusalReason.None) };
    var source = new TargetSchedulerManager.App.Shared.TsSource("L", "C", () => false);
    var gate = new TargetSchedulerManager.App.Shared.TsEditGate(source, _ => ed);
    var vm = new MainViewModel(gate);
    ReconciliationRow row = Make.Leaf(target: "A", desired: 10, planSeconds: 300, planTsKey: "ep-1");
    vm.SetRowsForTest([row]);
    vm.ToggleGroup((TargetGroupRow)vm.Rows[0]);

    Assert.True(await vm.SetPlanDesiredAsync(row, 25));
    Assert.Equal(25, row.Desired);
    Assert.Equal(25, vm.Rows.OfType<TargetGroupRow>().Single().Desired);
}
```

Add a shared stub at the bottom of `MainViewModelTests.cs` (so the test compiles — it mirrors the gate test's stub):

```csharp
internal sealed class TsEditGateTests_Stub : TargetSchedulerManager.App.Shared.ITsEditor
{
    public (Astronomy.Catalog.TargetScheduler.FieldEditResult? Result, Astronomy.Catalog.TargetScheduler.RefusalReason Refusal) Next;
    public (Astronomy.Catalog.TargetScheduler.FieldEditResult? Result, Astronomy.Catalog.TargetScheduler.RefusalReason Refusal) TrySetField(
        Astronomy.Catalog.TargetScheduler.TsTable table, string tsKey, string column, object? value) => Next;
    public void Dispose() { }
}
```

- [ ] **Step 2: Add `planTsKey` to `Make.Leaf`.** In `Make.cs`, add the parameter and pass it through (it's a trailing named arg — the `enabled`/`tsTargetKey`/`targetId` ctor params keep their defaults):

```csharp
        string? panelLabel = null,
        RowSource? panelSource = null,
        string? planTsKey = null) =>
        new(target, "proj", filter, purpose, planSeconds, diskSeconds, source, plane,
            desired, acquired, accepted, disk, planCount, badge, flagged, planHours, diskHours,
            mixed, isDetail, detail, panelKey, panelLabel, panelSource, planTsKey: planTsKey);
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test TargetSchedulerManager.slnx --nologo --filter "FullyQualifiedName~SetPlanDesired_AppliedWrite"`
Expected: FAIL — `MainViewModel` has no `internal MainViewModel(TsEditGate)` ctor.

- [ ] **Step 4: Rewire `MainViewModel.cs`.** Apply these edits:

**(a)** Replace the source/mode fields (≈51–59) — delete `_targetActiveEdits` stays, but remove `_tsMode`, `_liveDisabled`, `_tsProbed`, `_tsDbPath` and add the gate:

```csharp
    // The guarded TS write + the LIVE/LOCAL source state (probe + sticky-fall) live in TsEditGate/TsSource;
    // the view-model holds the gate and reads its Source for the load path and the radio bindings.
    private readonly TsEditGate _gate;
    private TsSource Source => _gate.Source;

    private readonly Dictionary<string, bool> _targetActiveEdits = new(StringComparer.OrdinalIgnoreCase);
```

**(b)** Add constructors (just before the `Rows` property):

```csharp
    public MainViewModel() : this(TsEditGate.CreateDefault()) { }

    /// <summary>Test seam: inject a gate backed by a stub editor + a probe-controlled <see cref="TsSource"/>.</summary>
    internal MainViewModel(TsEditGate gate) => _gate = gate;
```

**(c)** Replace the binding getters (`IsLiveSelected`/`IsLocalSelected`/`LiveEnabled`/`TsSourceTooltip`) to read from `Source`:

```csharp
    public bool IsLiveSelected => Source.IsLive;
    public bool IsLocalSelected => !Source.IsLive;
    public bool LiveEnabled => Source.LiveEnabled;

    public string TsSourceTooltip => !Source.LiveEnabled
        ? "BIRDWATCHER unreachable this session — editing the LOCAL copy. Re-launch TSM to retry LIVE."
        : Source.IsLive
            ? "LIVE Target Scheduler db on BIRDWATCHER — edits hit the imaging rig immediately."
            : "LOCAL working copy — edits do NOT reach the rig until you copy it back.";
```

**(d)** Replace the probe block + path in `LoadAsync` (the `if (!_tsProbed) … else if …` block and the `_tsDbPath = …` line) with:

```csharp
        string tsDbPath = await Task.Run(Source.ResolvePathForLoad);
        RaiseTsSource();
        StatusText = $"scanning {DefaultLibrary} …";
        try
        {
            LoadResult result = await ReconciliationLoader.LoadAsync(DefaultLibrary, tsDbPath, DefaultToleranceDegrees);
            _lastLoad = result;
            _allRows = result.Rows;
            StatusText = $"library {DefaultLibrary}  ·  TS {tsDbPath} ({(Source.IsLive ? "LIVE BIRDWATCHER" : "local copy")})" +
                $"  ·  resolved in {result.Elapsed.TotalSeconds:0.0} s";
            ApplyFilters();
        }
```

**(e)** Replace `SetTsMode`:

```csharp
    public void SetTsMode(TsMode mode)
    {
        if (Source.TrySelectMode(mode)) { RaiseTsSource(); _ = LoadAsync(); }
        else RaiseTsSource();   // re-pin the radio to the active source
    }
```

**(f)** Delete the entire `ApplyFieldEditAsync` method (≈377–434) and replace it with the outcome mapper:

```csharp
    // Maps one guarded-write outcome to the status line + side effects, returning whether the value was applied.
    // A live drop greys the LIVE radio and reloads from LOCAL; a refusal/failure leaves the db untouched.
    private bool ApplyOutcome(EditOutcome outcome, string label)
    {
        switch (outcome)
        {
            case EditOutcome.Applied:
                return true;
            case EditOutcome.Refused refused:
                StatusText = $"can't change {label}: {RefusalText(refused.Reason)}";
                return false;
            case EditOutcome.Failed:
                StatusText = $"edit failed for {label} — see tsm.log";
                return false;
            case EditOutcome.LiveDropped:
                RaiseTsSource();
                StatusText = "BIRDWATCHER unreachable — switched to LOCAL for this session (re-launch TSM to retry LIVE).";
                _ = LoadAsync();
                return false;
            default:
                return false;
        }
    }

    private static string RefusalText(RefusalReason reason) => reason switch
    {
        RefusalReason.SchemaIncompatible => "TS db schema is incompatible",
        RefusalReason.ReadOnly => "TS db file is read-only",
        RefusalReason.OpenSidecar => "TS database busy (open in NINA?) — try again",
        RefusalReason.ColumnAbsent => "this TS db has no such column",
        _ => "refused",
    };
```

**(g)** Rewrite `SetTargetEnabledAsync` and `SetPlanDesiredAsync` to call the gate:

```csharp
    public async Task<bool> SetTargetEnabledAsync(TargetGroupRow group, bool enabled)
    {
        if (group.TsTargetKey is not string key)
            return false;
        EditOutcome outcome = await _gate.ApplyAsync(TsTable.Target, key, "active", enabled ? 1 : 0, group.Target);
        bool applied = ApplyOutcome(outcome, group.Target);
        if (applied) _targetActiveEdits[key] = enabled;
        return applied;
    }

    public async Task<bool> SetPlanDesiredAsync(ReconciliationRow row, int desired)
    {
        if (row.PlanTsKey is not string key)
            return false;
        EditOutcome outcome = await _gate.ApplyAsync(TsTable.ExposurePlan, key, "desired", desired, $"{row.Target} · {row.Filter}");
        if (!ApplyOutcome(outcome, $"{row.Target} · {row.Filter}"))
            return false;

        row.ApplyDesired(desired);
        TargetGroupRow? group = _groups.FirstOrDefault(g => g.Children.Contains(row));
        group?.Recompute();
        if (row.PanelKey is not null)
            group?.Panels?.FirstOrDefault(p => p.Children.Contains(row))?.Recompute();
        return true;
    }
```

**(h)** Add `using Astronomy.Catalog.TargetScheduler;` (for `TsTable`/`RefusalReason`/`EditOutcome` lives in `App.Shared` which is already imported; `TsTable` is from the library). Confirm the file still imports `Microsoft.Data.Sqlite` only if still used — after deleting `ApplyFieldEditAsync`, `SqliteConnection.ClearAllPools()` is gone from the VM, so **remove** `using Microsoft.Data.Sqlite;` and `using Astronomy.Catalog.TargetScheduler;` is needed for `TsTable`. Also delete the now-unused `TargetSchedulerEditor`/`FieldEditResult`/`TsTable`-related usings that are no longer referenced beyond `TsTable`.

- [ ] **Step 5: Run the full suite to verify pass + no regression**

Run: `dotnet build TargetSchedulerManager.slnx -v:m -nologo`
Expected: Build succeeded, 0 errors, 0 warnings.

Run: `dotnet test TargetSchedulerManager.slnx -v:q --nologo`
Expected: PASS — all existing App.Tests + the new `SetPlanDesired_AppliedWrite` + Tasks 2–3 tests (≈60 cases), 0 failures.

- [ ] **Step 6: Update `ARCHITECTURE.md`.** In the **Components** section under `TargetSchedulerManager`, append:

```markdown
  Guarded TS writes go through two App-side modules: **`TsSource`** (`Shared/`) owns the LIVE/LOCAL paths, the
  injected reachability probe, and the mode + sticky-disabled state (consulted by the load and the gate);
  **`TsEditGate`** (`Shared/`) is the one guarded write — `ApplyAsync(...) → EditOutcome`
  (`Applied`/`Refused`/`Failed`/`LiveDropped`) over an injected `ITsEditor`, delegating the sticky-fall to
  `TsSource` on a live drop. Both take their dependencies by injection, so the LIVE/LOCAL machine and the guarded
  write are unit-tested without SQLite or SMB. The library half is the consumer-neutral
  `TargetSchedulerEditor.TrySetField(...) → (FieldEditResult?, RefusalReason)`, which folds the four open-db guard
  predicates into one structured-refusal call (a future WriteBack app action reuses the same gate via an
  `ApplyPlanAsync` sibling).
```

- [ ] **Step 7: Commit**

```bash
git add TargetSchedulerManager.App/ViewModels/MainViewModel.cs TargetSchedulerManager.App.Tests/Make.cs TargetSchedulerManager.App.Tests/MainViewModelTests.cs ARCHITECTURE.md
git commit -m "refactor: route TS edits through TsEditGate/TsSource; delete ApplyFieldEditAsync"
```

---

## Deferred (out of scope — do NOT build now)

- **`TargetSchedulerWriter` guarded mirror + `TsEditGate.ApplyPlanAsync(WriteBackPlan)`** — lands with the WriteBack app action (its own feature, with plan-preview + manual-bucket UI). Building it now would be dead code (YAGNI). The gate's private envelope is the reuse point when it arrives.
- **Removing `MainViewModel.SetTsMode`'s reload-on-radio** or any LIVE/LOCAL UX change — behaviour is preserved 1:1.

## Self-Review

1. **Spec coverage.** Two modules (`TsSource` T2, `TsEditGate` T3) ✓; library guarded-apply + `RefusalReason` (T1) ✓; Gate-delegates-to-Source sticky-fall (`NotifyLiveWriteFailed`, T2/T3) ✓; concrete `ApplyAsync` + sealed `EditOutcome` (T3) ✓; probe + editor injected → tested machine (T2/T3 tests) ✓; VM thins, `ApplyFieldEditAsync` deleted (T4) ✓; docs with code (T1 Library, T4 ARCHITECTURE) ✓; `CONTEXT.md` deliberately not created ✓.
2. **Placeholder scan.** Every code step shows complete code; commands have expected output. None found.
3. **Type consistency.** `TrySetField → (FieldEditResult?, RefusalReason)` is used identically in T1/T3/T4. `EditOutcome` cases (`Applied`/`Refused`/`Failed`/`LiveDropped`) match across T3 (def) and T4 (`switch`). `TsSource` members (`CurrentPath`/`IsLive`/`LiveEnabled`/`ResolvePathForLoad`/`TrySelectMode`/`NotifyLiveWriteFailed`) match across T2 (def), T3 (gate), T4 (VM). `Make.Leaf(... planTsKey:)` (T4 step 2) feeds the T4 step-1 test. Consistent.

> **Note on Task 4 step (h):** the exact `using` set depends on what remains referenced after the rewire — the build in step 5 is the gate (0 warnings). If an unused-using warning appears, remove the named directive; if `TsTable` fails to resolve, ensure `using Astronomy.Catalog.TargetScheduler;` is present.
