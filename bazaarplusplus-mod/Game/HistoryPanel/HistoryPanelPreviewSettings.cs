#nullable enable
using BepInEx.Configuration;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static class HistoryPanelPreviewSettings
{
    private static ConfigEntry<bool>? _showSkillBoard;
    private static ConfigEntry<bool>? _showMonsterInfoBoard;
    private static ConfigEntry<bool>? _showBrandingBoard;
    private static ConfigEntry<bool>? _dynamicPreviewEnabled;

    public static void Initialize(ConfigFile config)
    {
        _showSkillBoard = config.Bind(
            "HistoryPanelPreview",
            "ShowSkillBoard",
            false,
            "Show the skill lane in History Panel card preview."
        );
        _showMonsterInfoBoard = config.Bind(
            "HistoryPanelPreview",
            "ShowMonsterInfoBoard",
            false,
            "Show the health/reward info panel in History Panel card preview."
        );
        _showBrandingBoard = config.Bind(
            "HistoryPanelPreview",
            "ShowBrandingBoard",
            false,
            "Show the branding strip in History Panel card preview."
        );
        _dynamicPreviewEnabled = config.Bind(
            "HistoryPanelPreview",
            "DynamicPreviewEnabled",
            true,
            "Continuously re-render the History Panel preview while visible."
        );
    }

    public static bool ShowSkillBoard => _showSkillBoard?.Value ?? false;

    public static bool ShowMonsterInfoBoard => _showMonsterInfoBoard?.Value ?? false;

    public static bool ShowBrandingBoard => _showBrandingBoard?.Value ?? false;

    public static bool DynamicPreviewEnabled => _dynamicPreviewEnabled?.Value ?? false;

    public static void SetDynamicPreviewEnabled(bool enabled)
    {
        if (_dynamicPreviewEnabled != null)
            _dynamicPreviewEnabled.Value = enabled;
    }

    public static bool ToggleDynamicPreviewEnabled()
    {
        var next = !DynamicPreviewEnabled;
        SetDynamicPreviewEnabled(next);
        return next;
    }
}
