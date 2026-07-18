#nullable enable
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Settings;

internal sealed class BppDockButtonNativeVisualState : MonoBehaviour
{
    private Button? _button;
    private Image? _targetImage;
    private BppDockButtonVisualState _nativeState;
    private bool _isInitialized;

    internal BppDockButtonVisualState? CapturedNativeState => _isInitialized ? _nativeState : null;

    internal void Initialize(Button button, BppDockButtonVisualState nativeState)
    {
        _button = button;
        if (!_isInitialized)
        {
            _nativeState = nativeState;
            _targetImage = nativeState.TargetGraphic as Image ?? button.targetGraphic as Image;
            _isInitialized = true;
        }

        ResetToNormal();
    }

    internal void ResetToNormal()
    {
        if (!_isInitialized || _button == null)
            return;

        ClearNativeSelection();
        if (_targetImage != null)
            _targetImage.sprite = _nativeState.ResolveNormalSprite();

        switch (_nativeState.Transition)
        {
            case Selectable.Transition.ColorTint:
                _button.colors = _nativeState.ResolveNormalColors();
                break;
            case Selectable.Transition.SpriteSwap:
                break;
            case Selectable.Transition.Animation:
                _button.animationTriggers = _nativeState.CloneAnimationTriggers();
                break;
        }
    }

    private void OnEnable()
    {
        if (_isInitialized)
            ResetToNormal();
    }

    private void ClearNativeSelection()
    {
        var eventSystem = EventSystem.current;
        if (
            _button != null
            && eventSystem != null
            && eventSystem.currentSelectedGameObject == _button.gameObject
        )
            eventSystem.SetSelectedGameObject(null);
    }
}
