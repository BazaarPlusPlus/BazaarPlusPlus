#nullable enable
using BazaarPlusPlus.Core.Config;
using BepInEx.Configuration;

namespace BazaarPlusPlus.Host;

/// <summary>Desktop settings store: binds each setting to an entry in the BepInEx config file.</summary>
internal sealed class BepInExSettingsStore : IBppSettingsStore
{
    private readonly ConfigFile _file;

    public BepInExSettingsStore(ConfigFile file)
    {
        _file = file ?? throw new ArgumentNullException(nameof(file));
    }

    public ISetting<T> Bind<T>(
        string section,
        string key,
        T defaultValue,
        string description,
        SettingRange<T>? acceptableRange = null
    )
        where T : IComparable
    {
        var acceptableValues =
            acceptableRange == null
                ? null
                : new AcceptableValueRange<T>(acceptableRange.Min, acceptableRange.Max);
        return new Setting<T>(
            _file.Bind(
                section,
                key,
                defaultValue,
                new ConfigDescription(description, acceptableValues)
            )
        );
    }

    private sealed class Setting<T> : ISetting<T>
    {
        private readonly ConfigEntry<T> _entry;

        public Setting(ConfigEntry<T> entry)
        {
            _entry = entry;
        }

        public T Value
        {
            get => _entry.Value;
            set => _entry.Value = value;
        }
    }
}
