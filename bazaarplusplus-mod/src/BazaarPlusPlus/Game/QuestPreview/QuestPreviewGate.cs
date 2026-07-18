#nullable enable
using BazaarPlusPlus.Patches;

namespace BazaarPlusPlus.Game.QuestPreview;

internal static class QuestPreviewGate
{
    internal static bool IsEnabled()
    {
        return BppPatchHost.Services.Config.EnableQuestPreviewConfig?.Value == true;
    }
}
