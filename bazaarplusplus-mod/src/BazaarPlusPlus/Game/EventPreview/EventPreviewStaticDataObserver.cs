#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.Game.EventPreview;

internal sealed class EventPreviewStaticDataObserver : MonoBehaviour
{
    private EncounterPreviewPlanPublisher? _plans;

    internal void Initialize(EncounterPreviewPlanPublisher plans) =>
        _plans = plans ?? throw new ArgumentNullException(nameof(plans));

    private void Update() => _plans?.ObserveStaticData();

    private void OnDestroy() => _plans = null;
}
