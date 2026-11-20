#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel.Data;

// Local rows use Saved or Unavailable. Ghost rows can use every value.
public enum ReplayAvailability
{
    Remote,
    Saved,
    Expired,
    Unavailable,
}
