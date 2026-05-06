#nullable enable
using System;

namespace BazaarPlusPlus.Game.Identity;

public sealed class PlayerObservationStore
{
    private const int CurrentSchemaVersion = 1;
    private readonly string _identityDirectoryPath;

    public PlayerObservationStore(string identityDirectoryPath)
    {
        _identityDirectoryPath = identityDirectoryPath
            ?? throw new ArgumentNullException(nameof(identityDirectoryPath));
    }

    public bool TryLoad(out PlayerObservationRecord? record)
    {
        record = null;
        if (
            !IdentityJsonFileStore.TryRead<PlayerObservationFile>(
                IdentityJsonFileStore.ObservationPath(_identityDirectoryPath),
                out var payload
            )
            || payload == null
            || !payload.IsValid()
        )
        {
            return false;
        }

        record = new PlayerObservationRecord(
            payload.PlayerAccountId!,
            payload.PlayerUsername!,
            payload.ObservedAtUtc!
        );
        return true;
    }

    public void Save(PlayerObservationRecord record)
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));

        IdentityJsonFileStore.Write(
            IdentityJsonFileStore.ObservationPath(_identityDirectoryPath),
            new PlayerObservationFile
            {
                SchemaVersion = CurrentSchemaVersion,
                PlayerAccountId = record.PlayerAccountId,
                PlayerUsername = record.PlayerUsername,
                ObservedAtUtc = record.ObservedAtUtc,
            }
        );
    }

    private sealed class PlayerObservationFile
    {
        public int SchemaVersion { get; set; }

        public string? PlayerAccountId { get; set; }

        public string? PlayerUsername { get; set; }

        public string? ObservedAtUtc { get; set; }

        public bool IsValid() =>
            SchemaVersion == CurrentSchemaVersion
            && !string.IsNullOrWhiteSpace(PlayerAccountId)
            && !string.IsNullOrWhiteSpace(PlayerUsername)
            && !string.IsNullOrWhiteSpace(ObservedAtUtc);
    }
}
