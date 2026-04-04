using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal sealed partial class CombatStatusBar : MonoBehaviour
{
    private float _visualBlend;

    private void OnEnable()
    {
        Events.CombatStarted.AddListener(OnCombatStarted, this);
        Events.CombatEnded.AddListener(OnCombatEnded, this);
        EnsureConfigStateInitialized();
        EnsureUi();
        RefreshUi();
    }

    private void OnDisable()
    {
        Events.CombatStarted.RemoveListener(OnCombatStarted);
        Events.CombatEnded.RemoveListener(OnCombatEnded);
        SetUiVisible(false);
    }

    private void OnDestroy()
    {
        DisposeUi();
    }

    private void Update()
    {
        EnsureConfigStateInitialized();

        _visualBlend = AdvanceVisualBlend(
            _visualBlend,
            IsCombatPlaybackActive,
            Time.unscaledDeltaTime
        );

        EnsureUi();
        RefreshUi();
    }

    private bool ShouldDraw()
    {
        EnsureConfigStateInitialized();
        return ShouldRenderForState(IsEnabled());
    }

    private static void OnCombatStarted()
    {
        BeginCombatPlayback();
    }

    private static void OnCombatEnded()
    {
        EndCombatPlayback();
    }
}
