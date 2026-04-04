#nullable enable
using BazaarPlusPlus.Core.Runtime;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal static class MonsterPreviewFeature
{
    public static bool IsEnabled => true;

    public static bool UseNativePreview =>
        BppRuntimeHost.Config.UseNativeMonsterPreviewConfig?.Value ?? false;

    public static bool UseCustomLivePreview => !UseNativePreview;
}
