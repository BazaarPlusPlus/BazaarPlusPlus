#nullable enable
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Infrastructure.UiTokens;

internal static class UiHover
{
    private static readonly ConditionalWeakTable<Button, ButtonState> ButtonStates = new();

    public static void ApplyButtonPalette(Button button, Color background, Color textColor)
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
        state.Border = Colors.ButtonBorderFor(background);
        state.HoverBackground = Colors.ButtonHoverBackgroundFor(background);
        state.PressedBackground = Colors.ButtonPressedBackgroundFor(background);
        state.HoverBorder = Colors.ButtonHoverBorderFor(background);
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
