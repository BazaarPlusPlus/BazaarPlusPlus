#nullable enable
using BazaarPlusPlus.Game.RunLogging.Models;

namespace BazaarPlusPlus.Game.RunLogging.Persistence;

public interface IRunLogStore
{
    RunLogSessionState? TryResumeActiveRun();

    RunLogSessionState CreateRun(RunLogCreateRequest request);

    void AppendEvent(string runId, RunLogEvent entry);

    void SaveCheckpoint(string runId, RunLogCheckpoint checkpoint);

    void CompleteRun(string runId, RunLogCompletion completion);

    void MarkRunAbandoned(string runId, RunLogAbandonment abandonment);
}
