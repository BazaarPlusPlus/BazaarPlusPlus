#nullable enable
using System;
using BazaarPlusPlus.Game.CombatReplay;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal interface IHistoryPanelRuntime
{
    bool IsInGameRun { get; }

    string? CurrentServerRunId { get; }

    string RunLogDatabasePath { get; }

    string CombatReplayDirectoryPath { get; }

    Func<CombatReplayRuntime?> CombatReplayRuntimeAccessor { get; }
}
