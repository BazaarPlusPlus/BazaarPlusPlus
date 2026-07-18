#nullable enable
using Newtonsoft.Json;

namespace BazaarPlusPlus.BazaarAgent;

/// <summary>
/// Executes one dequeued replay control command against the main-thread sink and maps the
/// outcome onto the wire contract. Status codes reuse the existing error code table:
/// 202 accepted (record) / 200 accepted (continue) / 400 <c>invalid</c> /
/// 409 <c>stale-or-unavailable</c> / 503 <c>unavailable</c> / 500 <c>internal</c>.
/// </summary>
public static class BazaarAgentReplayControlProcessor
{
    public static void Process(
        BazaarAgentPendingCommand<BazaarAgentReplayCommand> pending,
        IBazaarAgentReplayControlSink sink,
        IBazaarAgentLogger logger
    )
    {
        var command = pending.Command;
        BazaarAgentReplayControlOutcome outcome;
        try
        {
            outcome =
                command.Kind == BazaarAgentReplayControlKind.Start
                    ? sink.Start(
                        pending.RequestId,
                        command.Payload ?? Array.Empty<byte>(),
                        command.BattleId
                    )
                    : sink.Continue();
        }
        catch (Exception ex)
        {
            // Answer BEFORE logging — the claimed command's timeout is disarmed, and the
            // logger itself can throw; the response must never be lost to a logging failure.
            pending.SetResponse(
                new BazaarAgentServerResponse(500, BuildErrorBody("internal", ex.GetType().Name))
            );
            logger.TryEmit(
                BazaarAgentLogEvents.ReplayRequestFailed(
                    pending.RequestId,
                    command.Kind,
                    command.BattleId,
                    BazaarAgentLogReasonCode.ReplaySinkException,
                    ex
                )
            );
            return;
        }

        // Complete the HTTP response before logging: the logger itself may throw, and every
        // claimed command must still receive exactly one response.
        pending.SetResponse(MapOutcome(command.Kind, outcome));
        if (outcome.Status != BazaarAgentReplayControlStatus.Accepted)
        {
            logger.TryEmit(
                BazaarAgentLogEvents.ReplayRequestFailed(
                    pending.RequestId,
                    command.Kind,
                    outcome.BattleId,
                    MapFailureReason(outcome.Status),
                    exception: null
                )
            );
        }
    }

    public static BazaarAgentServerResponse MapOutcome(
        BazaarAgentReplayControlKind kind,
        BazaarAgentReplayControlOutcome outcome
    )
    {
        return outcome.Status switch
        {
            BazaarAgentReplayControlStatus.Accepted
                when kind == BazaarAgentReplayControlKind.Start => new BazaarAgentServerResponse(
                202,
                JsonConvert.SerializeObject(
                    new Dictionary<string, object?>
                    {
                        ["accepted"] = true,
                        ["battleId"] = outcome.BattleId,
                        ["status"] = "recording-started",
                    }
                )
            ),
            BazaarAgentReplayControlStatus.Accepted => new BazaarAgentServerResponse(
                200,
                JsonConvert.SerializeObject(
                    new Dictionary<string, object?>
                    {
                        ["accepted"] = true,
                        ["status"] = "continue-triggered",
                    }
                )
            ),
            BazaarAgentReplayControlStatus.InvalidPayload => new BazaarAgentServerResponse(
                400,
                BuildErrorBody("invalid", outcome.FailureReason)
            ),
            BazaarAgentReplayControlStatus.Rejected => new BazaarAgentServerResponse(
                409,
                BuildErrorBody("stale-or-unavailable", outcome.FailureReason)
            ),
            _ => new BazaarAgentServerResponse(
                503,
                BuildErrorBody("unavailable", outcome.FailureReason)
            ),
        };
    }

    private static string BuildErrorBody(string code, string? details)
    {
        var envelope = new Dictionary<string, object?> { ["error"] = code };
        if (!string.IsNullOrEmpty(details))
            envelope["details"] = details;
        return JsonConvert.SerializeObject(envelope);
    }

    private static BazaarAgentLogReasonCode MapFailureReason(
        BazaarAgentReplayControlStatus status
    ) =>
        status switch
        {
            BazaarAgentReplayControlStatus.InvalidPayload =>
                BazaarAgentLogReasonCode.ReplayInvalidPayload,
            BazaarAgentReplayControlStatus.Rejected => BazaarAgentLogReasonCode.ReplayRejected,
            _ => BazaarAgentLogReasonCode.ReplayUnavailable,
        };
}
