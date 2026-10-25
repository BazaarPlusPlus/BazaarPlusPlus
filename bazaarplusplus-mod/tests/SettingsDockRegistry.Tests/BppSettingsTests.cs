using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Host;
using BepInEx.Configuration;
using Xunit;

namespace BazaarPlusPlus.Tests.SettingsDockRegistry;

public class BppSettingsTests
{
    [Fact]
    public void CombatStatusBarToggle_persists_visibility_without_resetting_speed()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-combat-status-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            var file = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppSettings(new BepInExSettingsStore(file));
            var toggle = CombatStatusBarSettingsDockEntry.Create().Build(config);
            Assert.True(toggle.ReadToggle!());

            config.CombatStatusBarSpeedMultiplierConfig.Value = 0.67f;
            toggle.WriteToggle!(false);
            file.Save();

            var reloaded = new BppSettings(
                new BepInExSettingsStore(new ConfigFile(configPath, saveOnInit: false))
            );
            Assert.False(CombatStatusBarSettingsDockEntry.Create().Build(reloaded).ReadToggle!());
            Assert.Equal(0.67f, reloaded.CombatStatusBarSpeedMultiplierConfig.Value);
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void DetailedPreviewConfig_defaults_on_with_one_key()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-quest-preview-default-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppSettings(new BepInExSettingsStore(configFile));

            Assert.True(config.EnableDetailedPreviewConfig.Value);
            var entry = configFile[new ConfigDefinition("DetailedPreview", "Enabled")];
            config.EnableDetailedPreviewConfig.Value = false;
            Assert.Equal(false, entry.BoxedValue);
            Assert.Contains("quest completion reward effects", entry.Description.Description);
            Assert.Contains("aggregate-item missing-type hints", entry.Description.Description);
            Assert.DoesNotContain(
                configFile.Keys,
                definition =>
                    definition.Section is "EventPreview" or "QuestPreview" or "QuestRewardPreview"
            );
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void Ranged_settings_clamp_writes_to_their_acceptable_range()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-ranged-settings-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            var config = new BppSettings(
                new BepInExSettingsStore(new ConfigFile(configPath, saveOnInit: false))
            );

            config.GraphicsUpscalingSharpnessConfig.Value = 2f;
            config.VoiceSubtitlesEnglishFontScaleConfig.Value = 0.5f;

            Assert.Equal(1f, config.GraphicsUpscalingSharpnessConfig.Value);
            Assert.Equal(1f, config.VoiceSubtitlesEnglishFontScaleConfig.Value);
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void DetailedPreviewConfig_starts_on_even_when_both_old_switches_were_off()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-quest-preview-no-migration-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            File.WriteAllText(
                configPath,
                "[EventPreview]\nEnabled = false\n[QuestPreview]\nEnabled = false\n"
            );
            var config = new BppSettings(
                new BepInExSettingsStore(new ConfigFile(configPath, saveOnInit: false))
            );

            Assert.True(config.EnableDetailedPreviewConfig!.Value);
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }
}
