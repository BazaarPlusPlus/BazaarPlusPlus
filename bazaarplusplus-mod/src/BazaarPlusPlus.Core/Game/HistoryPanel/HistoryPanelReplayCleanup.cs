#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel;

public readonly record struct ReplayPayloadCleanupResult(
    int FailedBattleCount,
    Exception? Exception
);

public static class HistoryPanelReplayCleanup
{
    public static ReplayPayloadCleanupResult Execute(
        IReadOnlyList<string> battleIds,
        Action<string> deleteLocalPayload,
        Action<string> deleteGhostPayload
    )
    {
        var failedBattleCount = 0;
        Exception? firstException = null;
        foreach (var battleId in battleIds)
        {
            var failed = false;
            try
            {
                deleteLocalPayload(battleId);
            }
            catch (Exception ex)
            {
                failed = true;
                firstException ??= ex;
            }

            try
            {
                deleteGhostPayload(battleId);
            }
            catch (Exception ex)
            {
                failed = true;
                firstException ??= ex;
            }

            if (failed)
                failedBattleCount++;
        }

        return new ReplayPayloadCleanupResult(failedBattleCount, firstException);
    }
}
