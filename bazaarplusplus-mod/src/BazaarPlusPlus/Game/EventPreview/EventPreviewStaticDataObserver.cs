#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.Game.EventPreview;

internal sealed class EventPreviewStaticDataObserver : MonoBehaviour
{
    private EncounterPreviewModule? _module;

    internal void Initialize(EncounterPreviewModule module) =>
        _module = module ?? throw new ArgumentNullException(nameof(module));

    private void Update() => _module?.ObserveStaticData();

    private void OnDestroy() => _module = null;
}
