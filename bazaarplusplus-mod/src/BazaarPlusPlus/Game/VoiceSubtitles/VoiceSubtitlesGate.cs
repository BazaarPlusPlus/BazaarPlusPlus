#nullable enable
using BazaarPlusPlus.Patches;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal static class VoiceSubtitlesGate
{
    internal static bool IsEnabled()
    {
        return BppPatchHost.Services.Config.EnableVoiceSubtitlesConfig?.Value == true;
    }
}
