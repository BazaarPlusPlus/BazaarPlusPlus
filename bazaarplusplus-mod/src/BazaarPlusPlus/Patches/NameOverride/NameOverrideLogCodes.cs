#nullable enable

namespace BazaarPlusPlus.Patches.NameOverride;

internal enum NameOverrideOperation
{
    ResolveProfile,
    DisplayUsername,
    UpdatePlayer,
    SetHeroName,
}

internal enum NameOverrideReasonCode
{
    ProfileUnavailable,
    Replaced,
}
