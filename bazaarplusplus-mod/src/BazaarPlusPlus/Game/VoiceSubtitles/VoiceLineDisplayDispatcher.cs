#nullable enable

using UnityEngine;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal sealed class VoiceLineDisplayDispatcher : MonoBehaviour
{
    private void Update()
    {
        VoiceLineDisplay.ProcessQueuedShows();
    }
}
