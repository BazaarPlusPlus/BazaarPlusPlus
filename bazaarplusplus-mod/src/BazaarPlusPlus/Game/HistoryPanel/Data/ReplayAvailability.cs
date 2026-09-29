#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel.Data;

// Whether History can replay a battle. Ghost rows reach every value; local rows are only
// Saved or Unavailable. Storage strings are owned by ReplayAvailabilityCodec.
internal enum ReplayAvailability
{
    Remote,
    Saved,
    Expired,
    Unavailable,
}
