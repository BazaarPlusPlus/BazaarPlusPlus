#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.Core.Runtime;
using TheBazaar;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace BazaarPlusPlus.Game.Input;

internal static class BppHotkeyService
{
    private const string KeyboardPrefix = "<Keyboard>/";
    private const string MousePrefix = "<Mouse>/";
    private const string CtrlAliasPath = "<Keyboard>/ctrl";
    private const string ShiftAliasPath = "<Keyboard>/shift";
    private const string LeftMouseButtonName = "leftButton";
    private const string RightMouseButtonName = "rightButton";
    private const string MiddleMouseButtonName = "middleButton";
    private const string BackMouseButtonName = "backButton";
    private const string ForwardMouseButtonName = "forwardButton";

    private static readonly IReadOnlyDictionary<string, string> BindingDisplayAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [CtrlAliasPath] = "Ctrl",
            [ShiftAliasPath] = "Shift",
            [MousePrefix + LeftMouseButtonName] = "LMB",
            [MousePrefix + RightMouseButtonName] = "RMB",
            [MousePrefix + MiddleMouseButtonName] = "MMB",
            [MousePrefix + BackMouseButtonName] = "BACK",
            [MousePrefix + ForwardMouseButtonName] = "FORWARD",
        };

    private static readonly IReadOnlyDictionary<BppHotkeyActionId, string> DefaultBindingPaths =
        new Dictionary<BppHotkeyActionId, string>
        {
            [BppHotkeyActionId.HoldEnchantPreview] = CtrlAliasPath,
            [BppHotkeyActionId.HoldUpgradePreview] = ShiftAliasPath,
        };

    private static readonly Dictionary<string, InputAction> CachedActions = new(
        StringComparer.OrdinalIgnoreCase
    );
    private static readonly HashSet<string> LoggedInvalidBindingPaths = new(
        StringComparer.OrdinalIgnoreCase
    );
    private static readonly HashSet<string> LoggedUnresolvedBindingPaths = new(
        StringComparer.OrdinalIgnoreCase
    );
    private static readonly HashSet<string> LoggedModifierDisagreements = new(
        StringComparer.OrdinalIgnoreCase
    );

    internal static bool IsHeld(
        BppHotkeyActionId actionId,
        Keyboard? keyboard = null,
        Mouse? mouse = null
    )
    {
        keyboard ??= Keyboard.current;
        mouse ??= Mouse.current;

        return IsPressed(GetBindingPath(actionId), keyboard, mouse);
    }

    internal static bool WasPressedThisFrame(string bindingPath)
    {
        var normalized = NormalizeBindingPath(bindingPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            if (LoggedInvalidBindingPaths.Add(bindingPath ?? string.Empty))
            {
                BppLog.Warn(
                    "BppHotkeyService",
                    $"Rejected hotkey binding path '{bindingPath ?? "<null>"}' because it could not be normalized."
                );
            }

            return false;
        }

        var action = GetOrCreateAction(normalized);
        if (action.controls.Count == 0 && LoggedUnresolvedBindingPaths.Add(normalized))
        {
            BppLog.Warn(
                "BppHotkeyService",
                $"Hotkey binding '{normalized}' resolved to zero input controls after enabling its InputAction."
            );
        }

        return action.WasPressedThisFrame();
    }

    private static bool IsPressed(
        string bindingPath,
        Keyboard? keyboard = null,
        Mouse? mouse = null
    )
    {
        var normalized = NormalizeBindingPath(bindingPath);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (string.Equals(normalized, CtrlAliasPath, StringComparison.OrdinalIgnoreCase))
            return IsModifierPressed(
                normalized,
                () => KeyBindings.Modifiers.IsCtrlPressed(keyboard)
            );

        if (string.Equals(normalized, ShiftAliasPath, StringComparison.OrdinalIgnoreCase))
            return IsModifierPressed(
                normalized,
                () => KeyBindings.Modifiers.IsShiftPressed(keyboard)
            );

        if (TryFindSupportedMouseButton(normalized, mouse, out var button))
            return button.isPressed;

        return GetOrCreateAction(normalized).IsPressed();
    }

    internal static string GetBindingPath(BppHotkeyActionId actionId)
    {
        var configValue = GetConfigValue(actionId);
        var normalized = NormalizeBindingPath(configValue);
        return string.IsNullOrWhiteSpace(normalized) ? GetDefaultBindingPath(actionId) : normalized;
    }

    internal static string GetBindingDisplay(BppHotkeyActionId actionId)
    {
        return GetBindingDisplay(GetBindingPath(actionId));
    }

    internal static string GetBindingDisplay(string bindingPath)
    {
        var normalized = NormalizeBindingPath(bindingPath);
        if (BindingDisplayAliases.TryGetValue(normalized, out var alias))
            return alias;

        var display = InputControlPath.ToHumanReadableString(
            normalized,
            InputControlPath.HumanReadableStringOptions.OmitDevice
        );
        if (string.IsNullOrWhiteSpace(display))
            return normalized;

        return normalized.StartsWith(MousePrefix, StringComparison.OrdinalIgnoreCase)
            ? $"{display}"
            : display;
    }

    internal static bool UsesDefault(BppHotkeyActionId actionId)
    {
        return string.Equals(
            GetBindingPath(actionId),
            GetDefaultBindingPath(actionId),
            StringComparison.OrdinalIgnoreCase
        );
    }

    internal static void ResetToDefault(BppHotkeyActionId actionId)
    {
        SetConfigValue(actionId, GetDefaultBindingPath(actionId));
    }

    internal static bool TrySetBindingPath(
        BppHotkeyActionId actionId,
        string? bindingPath,
        out string? errorMessage
    )
    {
        var normalized = NormalizeBindingPath(bindingPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            errorMessage = BppKeybindLabelResolver.ResolveUnsupportedKey(
                PlayerPreferences.Data.LanguageCode
            );
            return false;
        }

        if (TryGetConflictingAction(actionId, normalized, out var conflictingAction))
        {
            errorMessage =
                $"{BppKeybindLabelResolver.ResolveActionLabel(actionId, PlayerPreferences.Data.LanguageCode)} conflicts with {BppKeybindLabelResolver.ResolveActionLabel(conflictingAction, PlayerPreferences.Data.LanguageCode)}";
            return false;
        }

        SetConfigValue(actionId, normalized);
        errorMessage = null;
        return true;
    }

    private static bool TryGetConflictingAction(
        BppHotkeyActionId actionId,
        string candidatePath,
        out BppHotkeyActionId conflictingAction
    )
    {
        var candidatePaths = new HashSet<string>(
            ExpandBindingPaths(candidatePath),
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var otherAction in DefaultBindingPaths.Keys)
        {
            if (otherAction == actionId)
                continue;

            if (candidatePaths.Overlaps(ExpandBindingPaths(GetBindingPath(otherAction))))
            {
                conflictingAction = otherAction;
                return true;
            }
        }

        conflictingAction = default;
        return false;
    }

    private static IEnumerable<string> ExpandBindingPaths(string bindingPath)
    {
        var normalized = NormalizeBindingPath(bindingPath);
        if (string.IsNullOrWhiteSpace(normalized))
            yield break;

        yield return normalized;

        if (string.Equals(normalized, CtrlAliasPath, StringComparison.OrdinalIgnoreCase))
        {
            yield return KeyboardPrefix + "leftCtrl";
            yield return KeyboardPrefix + "rightCtrl";
            yield break;
        }

        if (string.Equals(normalized, ShiftAliasPath, StringComparison.OrdinalIgnoreCase))
        {
            yield return KeyboardPrefix + "leftShift";
            yield return KeyboardPrefix + "rightShift";
        }
    }

    private static InputAction GetOrCreateAction(string bindingPath)
    {
        var normalized = NormalizeBindingPath(bindingPath);
        if (CachedActions.TryGetValue(normalized, out var existingAction))
            return existingAction;

        var action = new InputAction(type: InputActionType.Button);
        foreach (var expandedPath in ExpandBindingPaths(normalized))
            action.AddBinding(expandedPath);

        action.Enable();
        CachedActions[normalized] = action;
        return action;
    }

    private static bool IsModifierPressed(string normalizedBindingPath, Func<bool> legacyCheck)
    {
        var legacyPressed = legacyCheck();
        var actionPressed = GetOrCreateAction(normalizedBindingPath).IsPressed();
        if (
            legacyPressed != actionPressed
            && LoggedModifierDisagreements.Add(normalizedBindingPath)
        )
        {
            BppLog.Info(
                "BppHotkeyService",
                $"Modifier disagreement binding={normalizedBindingPath} legacy={legacyPressed} action={actionPressed}"
            );
        }

        return legacyPressed || actionPressed;
    }

    private static string NormalizeBindingPath(string? bindingPath)
    {
        if (string.IsNullOrWhiteSpace(bindingPath))
            return string.Empty;

        var trimmed = bindingPath.Trim();
        if (trimmed.StartsWith(KeyboardPrefix, StringComparison.OrdinalIgnoreCase))
            return KeyboardPrefix + trimmed[KeyboardPrefix.Length..];

        if (!trimmed.StartsWith(MousePrefix, StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        if (!TryGetSupportedMouseButtonName(trimmed, out var buttonName))
            return string.Empty;

        var normalized = MousePrefix + buttonName;
        if (IsExplicitlyUnsupportedMousePath(normalized))
            return string.Empty;

        if (
            TryFindMouseControl(buttonName, Mouse.current, out var control)
            && control is not ButtonControl
        )
        {
            return string.Empty;
        }

        return normalized;
    }

    private static bool TryFindSupportedMouseButton(
        string bindingPath,
        Mouse? mouse,
        out ButtonControl button
    )
    {
        button = default!;
        if (!TryGetSupportedMouseButtonName(bindingPath, out var buttonName) || mouse == null)
            return false;

        var control = buttonName switch
        {
            LeftMouseButtonName => mouse.leftButton,
            RightMouseButtonName => mouse.rightButton,
            MiddleMouseButtonName => mouse.middleButton,
            BackMouseButtonName => mouse.backButton,
            ForwardMouseButtonName => mouse.forwardButton,
            _ => null,
        };

        if (control == null || control.synthetic)
            return false;

        button = control;
        return true;
    }

    private static bool TryGetSupportedMouseButtonName(string bindingPath, out string buttonName)
    {
        buttonName = string.Empty;
        if (!bindingPath.StartsWith(MousePrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var rawButtonName = bindingPath[MousePrefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(rawButtonName))
            return false;

        if (TryFindMouseControl(rawButtonName, Mouse.current, out var control) && control != null)
            rawButtonName = control.name;

        return TryNormalizeSupportedMouseButtonName(rawButtonName, out buttonName);
    }

    private static bool TryFindMouseControl(
        string buttonName,
        Mouse? mouse,
        out InputControl? control
    )
    {
        control = mouse?.allControls.FirstOrDefault(candidate =>
            string.Equals(candidate.name, buttonName, StringComparison.OrdinalIgnoreCase)
        );
        return control != null;
    }

    private static bool IsExplicitlyUnsupportedMousePath(string bindingPath)
    {
        return bindingPath.Contains("scroll", StringComparison.OrdinalIgnoreCase)
            || bindingPath.Contains("position", StringComparison.OrdinalIgnoreCase)
            || bindingPath.Contains("delta", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNormalizeSupportedMouseButtonName(
        string buttonName,
        out string normalized
    )
    {
        normalized = buttonName.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (string.Equals(normalized, LeftMouseButtonName, StringComparison.OrdinalIgnoreCase))
        {
            normalized = LeftMouseButtonName;
            return true;
        }

        if (string.Equals(normalized, RightMouseButtonName, StringComparison.OrdinalIgnoreCase))
        {
            normalized = RightMouseButtonName;
            return true;
        }

        if (string.Equals(normalized, MiddleMouseButtonName, StringComparison.OrdinalIgnoreCase))
        {
            normalized = MiddleMouseButtonName;
            return true;
        }

        if (string.Equals(normalized, BackMouseButtonName, StringComparison.OrdinalIgnoreCase))
        {
            normalized = BackMouseButtonName;
            return true;
        }

        if (string.Equals(normalized, ForwardMouseButtonName, StringComparison.OrdinalIgnoreCase))
        {
            normalized = ForwardMouseButtonName;
            return true;
        }

        return false;
    }

    private static string GetDefaultBindingPath(BppHotkeyActionId actionId)
    {
        return DefaultBindingPaths[actionId];
    }

    private static string? GetConfigValue(BppHotkeyActionId actionId)
    {
        return GetConfigEntry(actionId)?.Value;
    }

    private static void SetConfigValue(BppHotkeyActionId actionId, string bindingPath)
    {
        var entry = GetConfigEntry(actionId);
        if (entry != null)
            entry.Value = bindingPath;
    }

    private static BepInEx.Configuration.ConfigEntry<string>? GetConfigEntry(
        BppHotkeyActionId actionId
    )
    {
        return actionId switch
        {
            BppHotkeyActionId.HoldEnchantPreview => BppRuntimeHost
                .Config
                .EnchantPreviewHotkeyPathConfig,
            BppHotkeyActionId.HoldUpgradePreview => BppRuntimeHost
                .Config
                .UpgradePreviewHotkeyPathConfig,
            _ => null,
        };
    }
}
