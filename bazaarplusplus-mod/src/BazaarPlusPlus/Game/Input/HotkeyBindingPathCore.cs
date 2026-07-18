#nullable enable

namespace BazaarPlusPlus.Game.Input;

// Pure binding-path algebra. This file must stay free of Unity/game-runtime references:
// it is compile-linked into HotkeyBindingPath.Tests together with BppHotkeyActionId.
internal static class HotkeyBindingPathCore
{
    internal const string KeyboardPrefix = "<Keyboard>/";
    internal const string MousePrefix = "<Mouse>/";
    internal const string CtrlAliasPath = KeyboardPrefix + "ctrl";
    internal const string ShiftAliasPath = KeyboardPrefix + "shift";
    internal const string LeftMouseButtonName = "leftButton";
    internal const string RightMouseButtonName = "rightButton";
    internal const string MiddleMouseButtonName = "middleButton";
    internal const string BackMouseButtonName = "backButton";
    internal const string ForwardMouseButtonName = "forwardButton";

    private static readonly IReadOnlyDictionary<string, string> SupportedMouseButtons =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [LeftMouseButtonName] = "LMB",
            [RightMouseButtonName] = "RMB",
            [MiddleMouseButtonName] = "MMB",
            [BackMouseButtonName] = "BACK",
            [ForwardMouseButtonName] = "FORWARD",
        };

    internal static IEnumerable<string> SupportedMouseButtonNames => SupportedMouseButtons.Keys;

    internal static IReadOnlyDictionary<string, string> DisplayAliases { get; } =
        CreateDisplayAliases();

    internal static IReadOnlyDictionary<BppHotkeyActionId, string> DefaultBindingPaths { get; } =
        new Dictionary<BppHotkeyActionId, string>
        {
            [BppHotkeyActionId.HoldEnchantPreview] = CtrlAliasPath,
            [BppHotkeyActionId.HoldUpgradePreview] = ShiftAliasPath,
            [BppHotkeyActionId.ToggleCollectionPanel] = KeyboardPrefix + "tab",
            [BppHotkeyActionId.ToggleLiveBuildPanel] = KeyboardPrefix + "capsLock",
            [BppHotkeyActionId.ToggleHistoryPanel] = KeyboardPrefix + "f8",
        };

    internal static string Normalize(string? bindingPath)
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
        return IsExplicitlyUnsupportedMousePath(normalized) ? string.Empty : normalized;
    }

    internal static IEnumerable<string> Expand(string normalizedPath)
    {
        var normalized = Normalize(normalizedPath);
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

    internal static bool IsModifierPath(string normalizedPath)
    {
        return normalizedPath.Contains("ctrl", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("shift", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("alt", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsExplicitlyUnsupportedMousePath(string bindingPath)
    {
        return bindingPath.Contains("scroll", StringComparison.OrdinalIgnoreCase)
            || bindingPath.Contains("position", StringComparison.OrdinalIgnoreCase)
            || bindingPath.Contains("delta", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool TryGetSupportedMouseButtonName(string bindingPath, out string buttonName)
    {
        buttonName = string.Empty;
        if (!bindingPath.StartsWith(MousePrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        return TryNormalizeSupportedMouseButtonName(
            bindingPath[MousePrefix.Length..].Trim(),
            out buttonName
        );
    }

    internal static bool TryNormalizeSupportedMouseButtonName(
        string buttonName,
        out string normalized
    )
    {
        normalized = buttonName.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        foreach (var supportedName in SupportedMouseButtonNames)
        {
            if (!string.Equals(normalized, supportedName, StringComparison.OrdinalIgnoreCase))
                continue;

            normalized = supportedName;
            return true;
        }

        return false;
    }

    internal static BppHotkeyActionId? FindConflict(
        BppHotkeyActionId candidateId,
        string normalizedCandidatePath,
        IReadOnlyDictionary<BppHotkeyActionId, string> currentPaths
    )
    {
        var candidatePaths = new HashSet<string>(
            Expand(normalizedCandidatePath),
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var otherAction in DefaultBindingPaths.Keys)
        {
            if (otherAction == candidateId)
                continue;

            if (candidatePaths.Overlaps(Expand(currentPaths[otherAction])))
                return otherAction;
        }

        return null;
    }

    internal static string GetDefault(BppHotkeyActionId actionId)
    {
        return DefaultBindingPaths[actionId];
    }

    private static IReadOnlyDictionary<string, string> CreateDisplayAliases()
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [CtrlAliasPath] = "Ctrl",
            [ShiftAliasPath] = "Shift",
        };
        foreach (var mouseButton in SupportedMouseButtons)
            aliases[MousePrefix + mouseButton.Key] = mouseButton.Value;

        return aliases;
    }
}
