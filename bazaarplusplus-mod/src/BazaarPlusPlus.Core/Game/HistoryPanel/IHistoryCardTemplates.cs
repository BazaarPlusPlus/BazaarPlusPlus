#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.HistoryPanel;

public interface IHistoryCardTemplates
{
    /// <summary>Prepare on the main thread before a Ghost download looks up templates on a worker.</summary>
    void EnsureBuilt();

    /// <summary>Any thread. Ghost sync can call this from a worker.</summary>
    bool Contains(Guid templateId);

    /// <summary>Main thread only because this reads the game's static data. Return the native member name for the socket effect at this tier.</summary>
    string? SocketEffectAttribute(Guid templateId, CardTier tier);
}
