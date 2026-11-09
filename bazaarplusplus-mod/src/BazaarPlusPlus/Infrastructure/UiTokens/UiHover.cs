#nullable enable
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Infrastructure.UiTokens;

internal static class UiHover
{
    private static readonly ConditionalWeakTable<Button, ButtonState> ButtonStates = new();

    public static void ApplyButtonPalette(
        Button button,
        Color background,
        Color textColor,
        Color? border = null,
        Color? hoverBorder = null,
        Color? hoverBackground = null,
        Color? pressedBackground = null
    )
    {
        var state = ButtonStates.GetValue(button, _ => new ButtonState());
        if (!state.Registered)
        {
            state.Registered = true;
            button.RegisterCallback<MouseEnterEvent>(_ =>
            {
                state.Hovered = true;
                RefreshButton(button, state);
            });
            button.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                state.Hovered = false;
                state.Pressed = false;
                RefreshButton(button, state);
            });
            button.RegisterCallback<MouseDownEvent>(_ =>
            {
                state.Pressed = true;
                RefreshButton(button, state);
            });
            button.RegisterCallback<MouseUpEvent>(_ =>
            {
                state.Pressed = false;
                RefreshButton(button, state);
            });
        }

        state.Background = background;
        state.TextColor = textColor;
        // Unless the caller names them: a gold hairline that strengthens on hover, and a fill
        // that lifts toward white on hover and sinks toward black on press (ADR-0026).
        state.Border = border ?? InGameTheme.WithAlpha(InGameTheme.Rule, .58f);
        state.HoverBackground =
            hoverBackground
            ?? InGameTheme.WithAlpha(
                Color.Lerp(background, Color.white, .12f),
                Mathf.Clamp01(background.a + .02f)
            );
        state.PressedBackground =
            pressedBackground
            ?? InGameTheme.WithAlpha(Color.Lerp(background, Color.black, .1f), background.a);
        state.HoverBorder = hoverBorder ?? InGameTheme.WithAlpha(InGameTheme.RuleStrong, .78f);
        RefreshButton(button, state);
    }

    private static void RefreshButton(Button button, ButtonState state)
    {
        var background = state.Background;
        var border = state.Border;
        if (button.enabledInHierarchy)
        {
            if (state.Pressed)
            {
                background = state.PressedBackground;
                border = state.HoverBorder;
            }
            else if (state.Hovered)
            {
                background = state.HoverBackground;
                border = state.HoverBorder;
            }
        }

        button.style.backgroundColor = background;
        button.style.color = state.TextColor;
        UiStyle.BorderColor(button.style, border);
    }

    private sealed class ButtonState
    {
        public bool Registered { get; set; }

        public bool Hovered { get; set; }

        public bool Pressed { get; set; }

        public Color Background { get; set; }

        public Color HoverBackground { get; set; }

        public Color PressedBackground { get; set; }

        public Color TextColor { get; set; }

        public Color Border { get; set; }

        public Color HoverBorder { get; set; }
    }
}
