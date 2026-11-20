#nullable enable
namespace BazaarPlusPlus.Core.Config;

// Bind each section and key once. Ranges are inclusive and the store clamps persisted writes.
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
