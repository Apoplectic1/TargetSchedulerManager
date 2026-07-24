using Astronomy.Catalog.TargetScheduler;
using Astronomy.Diagnostics;

namespace TargetSchedulerManager.App.Shared;

/// <summary>The minimal read/write surface the gate needs from a TS editor — the seam tests stub. The
/// production adapter wraps the library's <see cref="TargetSchedulerEditor"/>.</summary>
internal interface ITsEditor : IDisposable
{
    (FieldEditResult? Result, RefusalReason Refusal) TrySetField(TsTable table, string tsKey, string column, object? value);
    (bool Found, object? Value) ReadField(TsTable table, string tsKey, string column);
    bool IsFieldAvailable(TsTable table, string column);
    (bool Found, double? Value) ReadPlanEffectiveExposure(string tsPlanKey);
}

/// <summary>Production adapter: opens a real <see cref="TargetSchedulerEditor"/> on the given path.</summary>
internal sealed class TsEditorAdapter : ITsEditor
{
    private readonly TargetSchedulerEditor _editor;
    public TsEditorAdapter(string path) => _editor = new TargetSchedulerEditor(path);
    public (FieldEditResult? Result, RefusalReason Refusal) TrySetField(TsTable table, string tsKey, string column, object? value) =>
        _editor.TrySetField(table, tsKey, column, value);
    public (bool Found, object? Value) ReadField(TsTable table, string tsKey, string column) =>
        _editor.ReadField(table, tsKey, column);
    public bool IsFieldAvailable(TsTable table, string column) =>
        _editor.IsFieldAvailable(table, column);
    public (bool Found, double? Value) ReadPlanEffectiveExposure(string tsPlanKey) =>
        _editor.ReadPlanEffectiveExposure(tsPlanKey);
    public void Dispose() => _editor.Dispose();
}

/// <summary>One field edit for the gate: the field coordinate (table, TS key, column), the absolute value
/// to write, and the grid-style label ("M 81 · Ha") for logging and the journal.</summary>
internal sealed record TsFieldEdit(TsTable Table, string Key, string Column, object? Value, string Label);

/// <summary>The outcome of one guarded write — a sealed set so callers match exhaustively.</summary>
internal abstract record EditOutcome
{
    private EditOutcome() { }
    /// <summary>The write was applied to the local db, read-back verified, and journaled for push.</summary>
    public sealed record Applied(string? Old, object? New) : EditOutcome;
    /// <summary>The db was unsafe to write; the value was not changed.</summary>
    public sealed record Refused(RefusalReason Reason) : EditOutcome;
    /// <summary>The row was missing, or the read-back did not confirm the write.</summary>
    public sealed record Failed(bool Found, bool Verified) : EditOutcome;
}

/// <summary>
/// The single guarded write path, shared by every TS field edit. Always targets the <b>local</b> db
/// (<see cref="TsSync.LocalPath"/> — BIRDWATCHER is written only by an explicit push), through an injected
/// editor factory (the test seam). Runs off the UI thread; guard-checks, read-back verifies, audits to the
/// diagnostics log, and journals every verified write on the <see cref="TsSync"/> so it replays at push.
/// </summary>
internal sealed class TsEditGate
{
    private readonly TsSync _sync;
    private readonly Func<string, ITsEditor> _editorFactory;

    public TsEditGate(TsSync sync, Func<string, ITsEditor> editorFactory)
    {
        _sync = sync;
        _editorFactory = editorFactory;
    }

    /// <summary>The real gate: the default <see cref="TsSync"/> and the production editor adapter.</summary>
    public static TsEditGate CreateDefault() => new(TsSync.CreateDefault(), path => new TsEditorAdapter(path));

    /// <summary>The sync orchestrator this gate journals through — the view-model reads it for the load path,
    /// the badge, and push.</summary>
    public TsSync Sync => _sync;

    /// <summary>
    /// Reads the current values of every editable field of <paramref name="table"/> for the row keyed by
    /// <paramref name="key"/>, off the UI thread — the seed for a field-editor form. Fields the open db lacks
    /// (TS schema drift) are skipped, so the dictionary holds exactly the columns a form should render.
    /// Returns <c>null</c> when the row is missing or the read faults: the caller shows an error instead of a
    /// form with fabricated values (no defaults, fail-loud).
    /// </summary>
    public Task<IReadOnlyDictionary<string, object?>?> ReadFieldsAsync(TsTable table, string key, string label) =>
        Task.Run<IReadOnlyDictionary<string, object?>?>(() =>
        {
            try
            {
                using ITsEditor editor = _editorFactory(_sync.LocalPath);
                Dictionary<string, object?> values = new(StringComparer.OrdinalIgnoreCase);
                foreach (TsField field in TsEditableSchema.For(table))
                {
                    if (!editor.IsFieldAvailable(table, field.Column))
                    {
                        Log.Warn($"{table}.{field.Column} absent on this db — omitted from the \"{label}\" edit form");
                        continue;
                    }
                    (bool found, object? value) = editor.ReadField(table, key, field.Column);
                    if (!found)
                    {
                        Log.Error($"{table} read-seed for \"{label}\": row not found (key {key})");
                        return null;
                    }
                    values[field.Column] = value;
                }
                return values;
            }
            catch (Exception ex)
            {
                Log.Error($"{table} read-seed threw for \"{label}\"", ex);
                return null;
            }
        });

    /// <summary>Reads one plan's effective exposure (its own value — 0 taken literally — unless the negative
    /// defer-to-template sentinel, then the template default) as whole seconds, off the UI thread — the
    /// Seconds-cell mirror after a revert-to-default write. Null only when unknown (missing row/template, or
    /// a fault): the caller leaves the cell for the next reload.</summary>
    public Task<int?> ReadPlanEffectiveSecondsAsync(string key, string label) =>
        Task.Run<int?>(() =>
        {
            try
            {
                using ITsEditor editor = _editorFactory(_sync.LocalPath);
                (bool found, double? value) = editor.ReadPlanEffectiveExposure(key);
                return found && value >= 0 ? (int)Math.Round(value.Value) : null;
            }
            catch (Exception ex)
            {
                Log.Error($"effective-exposure read threw for \"{label}\"", ex);
                return null;
            }
        });

    /// <summary>Guard-checks and applies one field edit to the local TS db, off the UI thread; a verified
    /// write also journals for the next push. The one-element case of <see cref="ApplyManyAsync"/>.</summary>
    public async Task<EditOutcome> ApplyAsync(TsTable table, string key, string column, object? value, string label)
    {
        IReadOnlyList<EditOutcome> outcomes =
            await ApplyManyAsync([new TsFieldEdit(table, key, column, value, label)]);
        return outcomes[0];
    }

    /// <summary>
    /// Applies many field edits in one worker invocation on ONE editor session — the batch counterpart of
    /// <see cref="ApplyAsync"/> with the same per-edit contract: guard-check, read-back verify, journal,
    /// audit. Outcomes align with <paramref name="edits"/> by index; a faulted edit fails only itself and
    /// the batch continues. An editor that cannot open fails every edit (nothing was attempted).
    /// </summary>
    public Task<IReadOnlyList<EditOutcome>> ApplyManyAsync(IReadOnlyList<TsFieldEdit> edits) =>
        Task.Run<IReadOnlyList<EditOutcome>>(() =>
        {
            List<EditOutcome> outcomes = new(edits.Count);
            try
            {
                using ITsEditor editor = _editorFactory(_sync.LocalPath);
                foreach (TsFieldEdit edit in edits)
                    outcomes.Add(ApplyOne(editor, edit));
            }
            catch (Exception ex)
            {
                // The editor session itself failed (open/dispose) — every unattempted edit fails loudly.
                Log.Error($"edit batch aborted at {outcomes.Count}/{edits.Count}", ex);
                while (outcomes.Count < edits.Count)
                    outcomes.Add(new EditOutcome.Failed(Found: false, Verified: false));
            }
            return outcomes;
        });

    // The per-edit contract, shared by the single and batch paths.
    private EditOutcome ApplyOne(ITsEditor editor, TsFieldEdit edit)
    {
        try
        {
            (FieldEditResult? result, RefusalReason refusal) =
                editor.TrySetField(edit.Table, edit.Key, edit.Column, edit.Value);
            if (refusal != RefusalReason.None)
            {
                Log.Warn($"{edit.Table}.{edit.Column} write refused for \"{edit.Label}\": {refusal}");
                return new EditOutcome.Refused(refusal);
            }
            if (result is not { Succeeded: true })
            {
                Log.Error($"{edit.Table}.{edit.Column} write failed for \"{edit.Label}\" (found={result?.RowFound} verified={result?.Verified})");
                return new EditOutcome.Failed(result?.RowFound ?? false, result?.Verified ?? false);
            }

            _sync.RecordEdit(edit.Table, edit.Key, edit.Column, edit.Value, result.OldValue, edit.Label);
            Log.Info($"EDIT {edit.Table}.{edit.Column} \"{edit.Label}\": {result.OldValue} -> {edit.Value} on local {_sync.LocalPath} (journaled)");
            return new EditOutcome.Applied(result.OldValue, edit.Value);
        }
        catch (Exception ex)
        {
            Log.Error($"{edit.Table}.{edit.Column} write threw for \"{edit.Label}\"", ex);
            return new EditOutcome.Failed(Found: false, Verified: false);
        }
    }
}
