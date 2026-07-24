using Astronomy.Diagnostics;

namespace TargetSchedulerManager.App.Shared;

/// <summary>The one fire-and-forget seam for UI event handlers (review N3, completed): await the work,
/// log anything that escapes. Without this, an exception leaving an <c>async void</c> handler crashes
/// the app with nothing in tsm.log — the one place this project promises failures land. The awaited
/// callees all self-handle their domain failures; this guard makes that safety local and structural
/// instead of a non-local invariant every future callee edit could silently break.</summary>
internal static class UiTask
{
    public static async void FireAndLog(Func<Task> action, string what)
    {
        try { await action(); }
        catch (Exception ex) { Log.Error($"{what} failed unhandled", ex); }
    }
}
