#nullable enable
namespace BazaarPlusPlus.BazaarAgent;

internal readonly record struct BazaarAgentDispatchFailureResponse(
    int HttpStatus,
    string ErrorCode
);

internal static class BazaarAgentDispatchFailureMapper
{
    internal static BazaarAgentDispatchFailureResponse Map(
        BazaarAgentDispatchFailureKind failureKind
    ) =>
        failureKind switch
        {
            BazaarAgentDispatchFailureKind.Invalid => new(400, "invalid"),
            BazaarAgentDispatchFailureKind.Unavailable => new(503, "unavailable"),
            _ => new(500, "internal"),
        };
}
