#nullable enable
using System;
using BazaarPlusPlus.Game.RunLogging.Models;
using BazaarPlusPlus.Game.RunLogging.Upload;

namespace BazaarPlusPlus.Game.RunLogging.Persistence;

internal sealed class ReplicatedRunLogStore : IRunLogStore
{
    private readonly IRunLogStore _innerStore;
    private readonly RunUploadSqliteStore _uploadStore;

    public ReplicatedRunLogStore(IRunLogStore innerStore, RunUploadSqliteStore uploadStore)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _uploadStore = uploadStore ?? throw new ArgumentNullException(nameof(uploadStore));
    }

    public RunLogSessionState? TryResumeActiveRun()
    {
        return _innerStore.TryResumeActiveRun();
    }

    public RunLogSessionState CreateRun(RunLogCreateRequest request)
    {
        var session = _innerStore.CreateRun(request);
        _uploadStore.MarkRunDirty(request.RunId);
        return session;
    }

    public void AppendEvent(string runId, RunLogEvent entry)
    {
        _innerStore.AppendEvent(runId, entry);
        _uploadStore.MarkRunDirty(runId);
    }

    public void SaveCheckpoint(string runId, RunLogCheckpoint checkpoint)
    {
        _innerStore.SaveCheckpoint(runId, checkpoint);
        _uploadStore.MarkRunDirty(runId);
    }

    public void CompleteRun(string runId, RunLogCompletion completion)
    {
        _innerStore.CompleteRun(runId, completion);
        _uploadStore.MarkRunDirty(runId);
    }

    public void MarkRunAbandoned(string runId, RunLogAbandonment abandonment)
    {
        _innerStore.MarkRunAbandoned(runId, abandonment);
        _uploadStore.MarkRunDirty(runId);
    }
}
