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
                {
                    Log.Warn($"{table}.{column} write refused for \"{label}\": {refusal}");
                    return new EditOutcome.Refused(refusal);
                }
                if (result is not { Succeeded: true })
                {
                    Log.Error($"{table}.{column} write failed for \"{label}\" (found={result?.RowFound} verified={result?.Verified})");
                    return new EditOutcome.Failed(result?.RowFound ?? false, result?.Verified ?? false);
                }

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
