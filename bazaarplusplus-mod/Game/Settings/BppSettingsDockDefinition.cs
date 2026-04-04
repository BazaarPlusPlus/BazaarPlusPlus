#nullable enable
using System;

namespace BazaarPlusPlus.Game.Settings;

internal sealed class BppSettingsDockDefinition
{
    internal BppSettingsDockDefinition(
        string key,
        Func<string, string> resolveLabel,
        SettingsMenuToggleBridge bridge
    )
        : this(
            key,
            resolveLabel,
            _ => bridge.GetInitialValue() ? "ON" : "OFF",
            bridge.GetInitialValue,
            () => bridge.ApplyValue(!bridge.GetInitialValue()),
            collapseAfterActivate: false,
            requiresCtrlToActivate: false
        ) { }

    internal BppSettingsDockDefinition(
        string key,
        Func<string, string> resolveLabel,
        Func<string, string> resolveStatus,
        Func<bool> isActive,
        Action activate,
        bool collapseAfterActivate,
        bool requiresCtrlToActivate = false
    )
    {
        Key = !string.IsNullOrWhiteSpace(key)
            ? key
            : throw new ArgumentException("Key is required.", nameof(key));
        ResolveLabel = resolveLabel ?? throw new ArgumentNullException(nameof(resolveLabel));
        ResolveStatus = resolveStatus ?? throw new ArgumentNullException(nameof(resolveStatus));
        IsActive = isActive ?? throw new ArgumentNullException(nameof(isActive));
        Activate = activate ?? throw new ArgumentNullException(nameof(activate));
        CollapseAfterActivate = collapseAfterActivate;
        RequiresCtrlToActivate = requiresCtrlToActivate;
    }

    internal string Key { get; }

    internal Func<string, string> ResolveLabel { get; }

    internal Func<string, string> ResolveStatus { get; }

    internal Func<bool> IsActive { get; }

    internal Action Activate { get; }

    internal bool CollapseAfterActivate { get; }

    internal bool RequiresCtrlToActivate { get; }
}
