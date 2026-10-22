#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel;

public readonly struct DeleteConfirmation
{
    public DeleteConfirmation(string? runId, float expiresAt)
    {
        RunId = runId;
        ExpiresAt = expiresAt;
    }

    public string? RunId { get; }

    public float ExpiresAt { get; }

    public bool IsActiveFor(string? runId, float now)
    {
        return !string.IsNullOrWhiteSpace(runId)
            && string.Equals(RunId, runId, StringComparison.Ordinal)
            && now < ExpiresAt;
    }

    public bool HasExpired(float now)
    {
        return !string.IsNullOrWhiteSpace(RunId) && now >= ExpiresAt;
    }
}
