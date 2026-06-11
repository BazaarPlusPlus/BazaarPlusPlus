#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal sealed class BazaarDbSnapshotUploadSettingsDockEntry : ISettingsDockEntry
{
    public int Order => BppSettingsDockOrder.BazaarDbUpload;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "BazaarDbUpload",
            BazaarDbSnapshotUploadSettingsMenuLabel.Resolve,
            new SettingsMenuToggleBridge(
                () => ReadEnabled(config),
                enabled => WriteEnabled(config, enabled),
                BazaarDbSnapshotUploadController.OnEnabledChanged
            )
        );

    private static bool ReadEnabled(IBppConfig config) =>
        config.BazaarDbUploadEnabled?.Value ?? false;

    private static void WriteEnabled(IBppConfig config, bool enabled)
    {
        var entry = config.BazaarDbUploadEnabled;
        if (entry != null)
            entry.Value = enabled;
    }
}
