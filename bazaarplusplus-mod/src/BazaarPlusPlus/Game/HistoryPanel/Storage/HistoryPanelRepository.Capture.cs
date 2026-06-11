#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Storage;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.HistoryPanel.Storage;

internal sealed partial class HistoryPanelRepository
{
    private static readonly JsonSerializerSettings SerializerSettings =
        SerializerSettingsFactory.CreateSerializerSettings(includeStringEnumConverter: true);

    private static PvpBattleCardSetCapture DeserializeCapture(string json)
    {
        return JsonConvert.DeserializeObject<PvpBattleCardSetCapture>(json, SerializerSettings)
            ?? new PvpBattleCardSetCapture();
    }
}
