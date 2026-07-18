#nullable enable
using BazaarPlusPlus.Core.Runtime;

namespace BazaarPlusPlus.GameInterop.VoiceSubtitles;

internal sealed class VoiceSubtitlesInteropModule : IBppFeature
{
    public void Start()
    {
        VoiceLineVoObserverBridge.Reset();
    }

    public void Stop()
    {
        VoiceLineVoObserverBridge.Reset();
    }
}
