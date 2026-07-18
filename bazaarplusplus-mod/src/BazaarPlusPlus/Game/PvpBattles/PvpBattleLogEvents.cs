#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.PvpBattles;

internal enum PvpSnapshotCombatant
{
    Player,
    Opponent,
}

internal enum PvpSnapshotSection
{
    Hand,
    Skills,
}

internal enum PvpSnapshotReasonCode
{
    LiveReadException,
    OpeningMessageException,
    OpeningDataException,
}

[BppLogEventSource]
internal static class PvpBattleLogEvents
{
    internal static readonly BppLogFieldDefinition SnapshotDegradedCombatant = PublicLow(
        0,
        "combatant"
    );
    internal static readonly BppLogFieldDefinition SnapshotDegradedSection = PublicLow(
        1,
        "section"
    );
    internal static readonly BppLogFieldDefinition SnapshotDegradedBattleId = PublicHighShort(
        2,
        "battle_id"
    );
    internal static readonly BppLogFieldDefinition SnapshotDegradedReasonCode = PublicLow(
        3,
        "reason_code"
    );
    internal static readonly BppLogEventDefinition SnapshotDegraded = new(
        BppLogFeatureScope.PvpBattles,
        "pvp_battles.snapshot.degraded",
        [
            SnapshotDegradedCombatant,
            SnapshotDegradedSection,
            SnapshotDegradedBattleId,
            SnapshotDegradedReasonCode,
        ],
        new BppLogStormPolicy([
            SnapshotDegradedCombatant,
            SnapshotDegradedSection,
            SnapshotDegradedReasonCode,
        ])
    );

    private static BppLogFieldDefinition PublicLow(int order, string name) =>
        new(
            order,
            name,
            BppLogFieldPrivacy.Public,
            BppLogCorrelationPolicy.None,
            BppLogCardinality.Low
        );

    private static BppLogFieldDefinition PublicHighShort(int order, string name) =>
        new(
            order,
            name,
            BppLogFieldPrivacy.Public,
            BppLogCorrelationPolicy.Short,
            BppLogCardinality.High
        );
}
