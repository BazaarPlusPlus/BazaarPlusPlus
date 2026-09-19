using BazaarPlusPlus.Core.Config;
using BepInEx.Configuration;
using Xunit;

namespace BazaarPlusPlus.Tests.SettingsDockRegistry;

public class BppConfigTests
{
    [Fact]
    public void QuestPreviewConfig_defaults_off_with_its_only_key()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-quest-preview-default-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();

            config.Initialize(configFile);

            var entry = config.EnableQuestPreviewConfig;
            Assert.NotNull(entry);
            Assert.False(entry.Value);
            Assert.Equal("QuestPreview", entry.Definition.Section);
            Assert.Equal("Enabled", entry.Definition.Key);
            Assert.Contains("quest completion reward effects", entry.Description.Description);
            Assert.Contains("aggregate-item missing-type hints", entry.Description.Description);
            Assert.DoesNotContain(
                configFile.Keys,
                definition => definition.Section == "QuestRewardPreview"
            );
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void QuestPreviewConfig_does_not_read_or_migrate_the_old_contract()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-quest-preview-no-migration-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            File.WriteAllText(configPath, "[QuestRewardPreview]\nEnabled = true\n");
            var config = new BppConfig();

            config.Initialize(new ConfigFile(configPath, saveOnInit: false));

            Assert.False(config.EnableQuestPreviewConfig!.Value);
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }
}
