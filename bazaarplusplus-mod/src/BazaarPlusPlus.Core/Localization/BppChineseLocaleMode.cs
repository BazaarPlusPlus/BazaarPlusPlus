#nullable enable
namespace BazaarPlusPlus.Localization;

public enum BppChineseLocaleMode
{
    Mainland = 0,
    Taiwan = 1,

    // Legacy persisted config value. Runtime code normalizes this to Taiwan.
    [Obsolete("HongKong is kept only to migrate existing config values; use Taiwan.")]
    HongKong = 2,
}
