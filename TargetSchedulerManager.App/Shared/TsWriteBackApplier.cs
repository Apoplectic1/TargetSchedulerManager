using Astronomy.Catalog.TargetScheduler;

namespace TargetSchedulerManager.App.Shared;

/// <summary>The minimal surface the app needs from the library's write-back writer — the seam tests stub.
/// Carries the writer's safety predicates so callers refuse an incompatible/busy db before applying.
/// The production adapter wraps <see cref="TargetSchedulerWriter"/>.</summary>
internal interface ITsWriteBackApplier : IDisposable
{
    bool HasRequiredColumns { get; }
    bool IsReadOnly { get; }
    bool HasOpenSidecar { get; }
    WriteBackResult Execute(WriteBackPlan plan, bool apply);
}

/// <summary>Production adapter: opens a real <see cref="TargetSchedulerWriter"/> on the given path.</summary>
internal sealed class TsWriteBackAdapter : ITsWriteBackApplier
{
    private readonly TargetSchedulerWriter _writer;
    public TsWriteBackAdapter(string path) => _writer = new TargetSchedulerWriter(path);
    public bool HasRequiredColumns => _writer.HasRequiredColumns;
    public bool IsReadOnly => _writer.IsReadOnly;
    public bool HasOpenSidecar => _writer.HasOpenSidecar;
    public WriteBackResult Execute(WriteBackPlan plan, bool apply) => _writer.Execute(plan, apply);
    public void Dispose() => _writer.Dispose();
}
