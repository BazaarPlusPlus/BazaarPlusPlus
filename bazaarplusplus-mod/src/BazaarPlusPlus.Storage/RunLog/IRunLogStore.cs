#nullable enable
namespace BazaarPlusPlus.Storage.RunLog;

public interface IRunLogStore
{
    RunLogSessionState? TryResumeActiveRun();

    RunLogSessionState CreateRun(RunLogCreateRequest request);

    void AppendEvent(string runId, RunLogEvent entry);

    void SaveCheckpoint(string runId, RunLogCheckpoint checkpoint);

    void CompleteRun(string runId, RunLogCompletion completion);

    void MarkRunAbandoned(string runId, RunLogAbandonment abandonment);
}
