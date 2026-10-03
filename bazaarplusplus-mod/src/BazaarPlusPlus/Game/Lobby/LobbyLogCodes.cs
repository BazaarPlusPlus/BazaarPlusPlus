#nullable enable

namespace BazaarPlusPlus.Game.Lobby;

internal enum LobbyLogReasonCode
{
    HttpFailureStatus,
    ManifestVersionMissing,
    RequestTimedOut,
    RequestException,
    LabelRefreshException,
}
