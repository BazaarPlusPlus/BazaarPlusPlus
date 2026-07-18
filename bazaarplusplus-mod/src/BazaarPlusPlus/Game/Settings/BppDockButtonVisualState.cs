#nullable enable
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Settings;

internal readonly struct BppDockButtonVisualState(
    Selectable.Transition transition,
    ColorBlock colors,
    SpriteState spriteState,
    AnimationTriggers animationTriggers,
    Graphic? targetGraphic,
    Sprite? normalBaseSprite
)
{
    internal Selectable.Transition Transition { get; } = transition;

    internal ColorBlock Colors { get; } = colors;

    internal SpriteState SpriteState { get; } = spriteState;

    internal AnimationTriggers AnimationTriggers { get; } = animationTriggers;

    internal Graphic? TargetGraphic { get; } = targetGraphic;

    internal Sprite? NormalBaseSprite { get; } = normalBaseSprite;

    internal static BppDockButtonVisualState Capture(
        Selectable.Transition transition,
        ColorBlock colors,
        SpriteState spriteState,
        AnimationTriggers animationTriggers
    ) =>
        new(
            transition,
            colors,
            spriteState,
            animationTriggers,
            targetGraphic: null,
            normalBaseSprite: null
        );

    internal static BppDockButtonVisualState? Capture(
        Button? button,
        Sprite? normalBaseSprite = null
    )
    {
        if (button == null)
            return null;

        var targetImage = button.targetGraphic as Image ?? button.image;
        var spriteState = button.spriteState;

        return new(
            button.transition,
            button.colors,
            spriteState,
            button.animationTriggers,
            button.targetGraphic,
            normalBaseSprite ?? targetImage?.sprite
        );
    }

    // Opening the settings popover is not a persistent button selection. Resetting to this native
    // baseline lets SpriteSwap return from its hover override without exposing ClickedImage.
    internal ColorBlock ResolveNormalColors() => Colors;

    internal AnimationTriggers CloneAnimationTriggers()
    {
        var resolved = new AnimationTriggers
        {
            normalTrigger = AnimationTriggers.normalTrigger,
            highlightedTrigger = AnimationTriggers.highlightedTrigger,
            pressedTrigger = AnimationTriggers.pressedTrigger,
            selectedTrigger = AnimationTriggers.selectedTrigger,
            disabledTrigger = AnimationTriggers.disabledTrigger,
        };
        return resolved;
    }

    internal Sprite? ResolveNormalSprite() => NormalBaseSprite;

    internal void ApplyTo(Button button, Graphic fallbackTargetGraphic)
    {
        button.targetGraphic = TargetGraphic != null ? TargetGraphic : fallbackTargetGraphic;
        button.transition = Transition;
        button.colors = Colors;
        button.spriteState = SpriteState;
        button.animationTriggers = AnimationTriggers;
    }
}
