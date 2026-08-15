#nullable enable
namespace BazaarPlusPlus.ModApi.Bundle;

public enum RunBundleOpenFailureKind
{
    ContainerInvalid,
    PayloadInvalid,
    RunIdentityMismatch,
}

public sealed class RunBundleOpenResult
{
    private RunBundleOpenResult(
        OpenedRunBundleV5? value,
        RunBundleOpenFailureKind? failureKind,
        string? reason,
        Exception? exception
    )
    {
        Value = value;
        FailureKind = failureKind;
        Reason = reason;
        Exception = exception;
    }

    public OpenedRunBundleV5? Value { get; }

    public RunBundleOpenFailureKind? FailureKind { get; }

    public string? Reason { get; }

    public Exception? Exception { get; }

    public bool Succeeded => Value != null;

    internal static RunBundleOpenResult Success(OpenedRunBundleV5 value) =>
        new(value, null, null, null);

    internal static RunBundleOpenResult Failure(
        RunBundleOpenFailureKind failureKind,
        string reason,
        Exception? exception = null
    ) => new(null, failureKind, reason, exception);
}

public sealed class OpenedRunBundleV5
{
    internal OpenedRunBundleV5(OpenedBundleV5 bundle, RunPayloadV5 payload)
    {
        Bundle = bundle;
        Payload = payload;
    }

    public OpenedBundleV5 Bundle { get; }

    public RunPayloadV5 Payload { get; }

    public bool TryGetReplayableBattle(string battleId, out RunBattleV5? battle)
    {
        battle = null;
        if (string.IsNullOrWhiteSpace(battleId))
            return false;
        if (
            Payload.ReplayableBattleIds == null
            || Payload.Battles == null
            || !Payload.ReplayableBattleIds.Contains(battleId, StringComparer.Ordinal)
        )
            return false;

        RunBattleV5? matched = null;
        foreach (var candidate in Payload.Battles)
        {
            if (!string.Equals(candidate.BattleId, battleId, StringComparison.Ordinal))
                continue;
            if (matched != null)
                return false;
            matched = candidate;
        }

        if (matched == null || !RunBundleV5Contract.IsReplayable(matched))
            return false;
        battle = matched;
        return true;
    }
}

public static class RunBundleV5Contract
{
    private static readonly string[] RequiredCardSetLabels =
    {
        "player_hand",
        "player_skills",
        "opponent_hand",
        "opponent_skills",
    };

    // Every collection reached here is checked for null even where the DTO declares a non-nullable
    // collection with an initializer: well-formed MessagePack may encode `nil` for such a member,
    // and the deserializer writes that null straight over the initializer. Decoded DTOs are
    // untrusted input, so null-safe rejection is the contract, not defensive style.
    public static bool IsReplayable(RunBattleV5? battle)
    {
        if (
            battle?.Replay
                is not {
                    SpawnMessageBytes.Length: > 0,
                    CombatMessageBytes.Length: > 0,
                    DespawnMessageBytes.Length: > 0,
                }
            || battle.Snapshots?.CardSets == null
            || battle.Snapshots.CardSets.Count != RequiredCardSetLabels.Length
        )
            return false;

        foreach (var label in RequiredCardSetLabels)
        {
            BattleCardSetV5? matched = null;
            foreach (var cardSet in battle.Snapshots.CardSets)
            {
                if (!string.Equals(cardSet.Label, label, StringComparison.Ordinal))
                    continue;
                if (matched != null)
                    return false;
                matched = cardSet;
            }

            if (
                matched == null
                || string.Equals(matched.Status, "Missing", StringComparison.OrdinalIgnoreCase)
            )
                return false;
        }

        return true;
    }

    public static RunBundleOpenResult Open(ReadOnlyMemory<byte> bundleBytes)
    {
        OpenedBundleV5 bundle;
        try
        {
            bundle = BundleV5Codec.Open(bundleBytes);
        }
        catch (BundleV5Exception ex)
        {
            return RunBundleOpenResult.Failure(
                RunBundleOpenFailureKind.ContainerInvalid,
                ex.Reason,
                ex
            );
        }

        if (!RunPayloadV5Codec.TryDecode(bundle.RunPayload, out var payload, out var decodeReason))
        {
            return RunBundleOpenResult.Failure(
                RunBundleOpenFailureKind.PayloadInvalid,
                decodeReason ?? "run_payload_invalid"
            );
        }

        if (
            payload == null
            || !string.Equals(payload.RunId, bundle.Manifest.Run.RunId, StringComparison.Ordinal)
            || !string.Equals(
                payload.PlayerAccountId,
                bundle.Manifest.Run.PlayerAccountId,
                StringComparison.Ordinal
            )
        )
        {
            return RunBundleOpenResult.Failure(
                RunBundleOpenFailureKind.RunIdentityMismatch,
                "run_identity_mismatch"
            );
        }

        return RunBundleOpenResult.Success(new OpenedRunBundleV5(bundle, payload));
    }
}
