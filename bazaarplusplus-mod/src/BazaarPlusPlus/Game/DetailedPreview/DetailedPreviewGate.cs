#nullable enable
using BazaarPlusPlus.Patches;

namespace BazaarPlusPlus.Game.DetailedPreview;

internal static class DetailedPreviewGate
{
    internal static bool IsEnabled() =>
        BppPatchHost.Services.Config.EnableDetailedPreviewConfig.Value;
}
