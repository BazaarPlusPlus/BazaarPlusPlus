#nullable enable

namespace BazaarPlusPlus.GameInterop.Tooltips;

internal enum NativeTooltipControllerAuditDecision
{
    Audit,
    SkipInactiveNonAuthoritative,
    Unavailable,
}

internal readonly record struct NativeTooltipControllerAuditCandidate(
    bool IsAuthoritative,
    bool IsActiveInHierarchy,
    bool HasRequiredSurface
);

/// <summary>Pure authority/liveness policy for native tooltip clean-frame audits.</summary>
internal static class NativeTooltipControllerAuditCore
{
    internal static bool ShouldSkipInactiveNonAuthoritative(
        bool isAuthoritative,
        bool isActiveInHierarchy
    ) => !isAuthoritative && !isActiveInHierarchy;

    internal static NativeTooltipControllerAuditDecision Decide(
        NativeTooltipControllerAuditCandidate candidate
    )
    {
        if (
            ShouldSkipInactiveNonAuthoritative(
                candidate.IsAuthoritative,
                candidate.IsActiveInHierarchy
            )
        )
        {
            return NativeTooltipControllerAuditDecision.SkipInactiveNonAuthoritative;
        }

        return candidate.HasRequiredSurface
            ? NativeTooltipControllerAuditDecision.Audit
            : NativeTooltipControllerAuditDecision.Unavailable;
    }
}
