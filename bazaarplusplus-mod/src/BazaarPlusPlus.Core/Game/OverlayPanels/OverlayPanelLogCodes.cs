#nullable enable

namespace BazaarPlusPlus.Game.OverlayPanels;

internal enum OverlayTickFailureReasonCode
{
    CallbackException,
}

internal enum OverlayDirectiveFailureReasonCode
{
    CallbackException,
}

internal enum OverlayCombatProbeFailureReasonCode
{
    ReadFailed,
}

internal enum OverlayRequestRejectionReasonCode
{
    LifecycleCallbackRunning,
}

internal enum OverlayPanelRequestKind
{
    Open,
    Close,
    Escape,
}

internal enum OverlayPredicateKind
{
    Enabled,
    IsModalInputActive,
}
