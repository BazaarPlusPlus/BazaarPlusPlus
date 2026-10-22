#nullable enable
namespace BazaarPlusPlus.Infrastructure.SelfUpdate;

/// <summary>
/// At most one staging run per process. The main menu can be entered many times while a download
/// is still going; each visit joins the run in flight for that version instead of starting
/// another. A failed run is forgotten, so the next visit tries again.
/// </summary>
public static class SelfUpdateStaging
{
    private static readonly object Sync = new();
    private static string? _version;
    private static Task<ManagedPayloadStageResult>? _run;

    public static Task<ManagedPayloadStageResult> Ensure(
        string version,
        Func<Task<ManagedPayloadStageResult>> stage
    )
    {
        if (stage == null)
            throw new ArgumentNullException(nameof(stage));
        lock (Sync)
        {
            if (
                _run != null
                && _version == version
                && (
                    !_run.IsCompleted
                    || (_run.Status == TaskStatus.RanToCompletion && _run.Result.Succeeded)
                )
            )
                return _run;

            _version = version;
            _run = Task.Run(stage);
            return _run;
        }
    }
}
