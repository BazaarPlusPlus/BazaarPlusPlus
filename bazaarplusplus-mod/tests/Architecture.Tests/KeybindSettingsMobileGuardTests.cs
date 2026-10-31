using Xunit;

namespace Architecture.Tests;

public sealed class KeybindSettingsMobileGuardTests
{
    private const string RefreshSupport =
        "BazaarPlusPlus.Patches.Settings.BppKeybindSettingsPatchSupport";
    private const string TouchPlatform = "BazaarPlusPlus.GameInterop.Touch.TouchPlatform";

    // A mobile platform adds no key rows (input-and-settings.md). Every patch that starts the
    // key-row refresh must skip itself there through Prepare, or that one patch adds the rows.
    [Fact]
    public void Every_patch_that_adds_key_rows_is_off_on_a_mobile_platform()
    {
        ArchitectureRules.Holds(
            "A patch that calls BppKeybindSettingsPatchSupport.RunRefresh needs "
                + "Prepare() => !TouchPlatform.IsMobile.",
            build =>
            {
                var patches = build
                    .TypesIn("BazaarPlusPlus.Patches.Settings")
                    .Where(type =>
                        type.FullName != RefreshSupport
                        && type.Instructions.Any(op => op.Targets(RefreshSupport, "RunRefresh"))
                    )
                    .ToArray();
                Assert.True(
                    patches.Length >= 3,
                    $"{build.Configuration}: found {patches.Length} key-row patches, expected at least 3."
                );
                return patches
                    .Where(type =>
                        !type.Methods.Any(method =>
                            method.Name == "Prepare"
                            && method.Instructions.Any(op =>
                                op.Targets(TouchPlatform, "get_IsMobile")
                            )
                        )
                    )
                    .Select(type => $"{type.FullName} has no mobile Prepare guard");
            }
        );
    }
}
