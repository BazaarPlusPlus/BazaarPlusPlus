#nullable enable
namespace BazaarPlusPlus.Infrastructure.SelfUpdate;

// The main menu may re-enter during a download, so all visits share a process-wide staging flight.
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
