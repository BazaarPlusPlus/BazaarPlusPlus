#nullable enable
namespace BazaarPlusPlus.Core.Config;

// BepInEx's publicized SettingChanged is ambiguous, so poll Value to invalidate derived caches.
public interface ISetting<T>
{
    T Value { get; set; }
}
