#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Game.RunLogging.Models;

namespace BazaarPlusPlus.Game.RunLogging;

public sealed class RunLogCaptureService
{
    public RunLogEvent BuildPvpBattleRecordedEvent(RunLogPvpBattleInput input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        return new RunLogEvent
        {
            Kind = "pvp_combat_recorded",
            Day = input.Day,
            Hour = input.Hour,
            EncounterId = input.EncounterId,
            CombatKind = input.CombatKind,
            BattleId = input.BattleId,
            OpponentName = input.OpponentName,
        };
    }
}

public sealed class RunLogPvpBattleInput
{
    public int? Day { get; set; }

    public int? Hour { get; set; }

    public string? EncounterId { get; set; }

    public string? CombatKind { get; set; }

    public string? BattleId { get; set; }

    public string? OpponentName { get; set; }
}

public sealed class RunLogPlayerStatsSnapshot
{
    public int? MaxHealth { get; set; }

    public int? Prestige { get; set; }

    public int? Level { get; set; }

    public int? Income { get; set; }

    public int? Gold { get; set; }
}
