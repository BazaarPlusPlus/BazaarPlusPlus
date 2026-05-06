#nullable enable
using System;

namespace BazaarPlusPlus.Game.Identity;

public sealed class PlayerObservationRecord
{
    public PlayerObservationRecord(
        string playerAccountId,
        string playerUsername,
        string observedAtUtc
    )
    {
        PlayerAccountId =
            playerAccountId ?? throw new ArgumentNullException(nameof(playerAccountId));
        PlayerUsername = playerUsername ?? throw new ArgumentNullException(nameof(playerUsername));
        ObservedAtUtc = observedAtUtc ?? throw new ArgumentNullException(nameof(observedAtUtc));
    }

    public string PlayerAccountId { get; }

    public string PlayerUsername { get; }

    public string ObservedAtUtc { get; }
}
