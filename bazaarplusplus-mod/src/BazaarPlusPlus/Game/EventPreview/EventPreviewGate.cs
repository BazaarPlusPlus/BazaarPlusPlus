#nullable enable
using BazaarPlusPlus.Patches;

namespace BazaarPlusPlus.Game.EventPreview;

internal static class EventPreviewGate
{
    internal static bool IsEnabled()
    {
        return BppPatchHost.Services.Config.EnableEventPreviewConfig?.Value == true;
    }
}
