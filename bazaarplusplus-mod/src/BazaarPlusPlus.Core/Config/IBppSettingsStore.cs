#nullable enable
namespace BazaarPlusPlus.Core.Config;

/// <summary>
/// The host's settings file. A store binds each <c>section.key</c> once, reads the persisted value
/// or falls back to the default, and persists writes to <see cref="ISetting{T}.Value"/>. An
/// acceptable range, when given, is inclusive and the store clamps writes to it.
/// </summary>
public interface IBppSettingsStore
{
    ISetting<T> Bind<T>(
        string section,
        string key,
        T defaultValue,
        string description,
        SettingRange<T>? acceptableRange = null
    )
        where T : IComparable;
}

public sealed class SettingRange<T>
    where T : IComparable
{
    public SettingRange(T min, T max)
    {
        if (min.CompareTo(max) > 0)
            throw new ArgumentException("The minimum must not exceed the maximum.", nameof(min));

        Min = min;
        Max = max;
    }

    public T Min { get; }

    public T Max { get; }
}
