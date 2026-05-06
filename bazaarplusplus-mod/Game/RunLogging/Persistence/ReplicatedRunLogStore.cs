#nullable enable
using System;
using BazaarPlusPlus.Game.RunLogging.Models;
using BazaarPlusPlus.Game.RunLogging.Upload;

namespace BazaarPlusPlus.Game.RunLogging.Persistence;

internal sealed class ReplicatedRunLogStore : IRunLogStore
{
    private readonly IRunLogStore _innerStore;
    private readonly RunSyncStateSqliteStore _syncStateStore;

    public ReplicatedRunLogStore(IRunLogStore innerStore, RunSyncStateSqliteStore syncStateStore)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _syncStateStore = syncStateStore ?? throw new ArgumentNullException(nameof(syncStateStore));
    }

    public RunLogSessionState? TryResumeActiveRun()
    {
        return _innerStore.TryResumeActiveRun();
    }

    public RunLogSessionState CreateRun(RunLogCreateRequest request)
    {
        var session = _innerStore.CreateRun(request);
        _syncStateStore.MarkRunDirty(request.RunId);
        return session;
    }

    public void AppendEvent(string runId, RunLogEvent entry)
    {
        _innerStore.AppendEvent(runId, entry);
        _syncStateStore.MarkRunDirty(runId);
    }

    public void SaveCheckpoint(string runId, RunLogCheckpoint checkpoint)
    {
        _innerStore.SaveCheckpoint(runId, checkpoint);
        _syncStateStore.MarkRunDirty(runId);
    }

    public void CompleteRun(string runId, RunLogCompletion completion)
    {
        _innerStore.CompleteRun(runId, completion);
        _syncStateStore.MarkRunDirty(runId);
    }

    public void MarkRunAbandoned(string runId, RunLogAbandonment abandonment)
    {
        _innerStore.MarkRunAbandoned(runId, abandonment);
        _syncStateStore.MarkRunDirty(runId);
    }
}
