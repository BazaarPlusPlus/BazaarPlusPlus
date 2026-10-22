#nullable enable

namespace BazaarPlusPlus.Game.Lobby;

public enum LobbyLogReasonCode
{
    HttpFailureStatus,
    ManifestVersionMissing,
    RequestTimedOut,
    RequestException,
    LabelRefreshException,
}
