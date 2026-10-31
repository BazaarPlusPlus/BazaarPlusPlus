using System.Reflection;
using Architecture.Tests;
using Xunit;

namespace AndroidHost.Tests;

public sealed class AndroidNativeArtBoundaryTests
{
    [Fact]
    public void Native_skin_art_has_one_runtime_owner_and_shared_ui_never_imports_collection()
    {
        var path = typeof(AndroidNativeArtBoundaryTests)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "BppAndroidHostAssembly")
            .Value!;
        var host = CompiledAssembly.Load(path);
        var accesses = host
            .Types.SelectMany(type =>
                type.Methods.SelectMany(method =>
                    method
                        .Instructions.Where(op => op.IsMemberAccess)
                        .Select(op => (type, method, op))
                )
            )
            .ToArray();
        var art = accesses
            .Where(access =>
                access.op.TargetType?.FullName == "Il2CppTheBazaar.GenericPopup"
                || (
                    access.op.TargetType?.FullName
                        == "Il2CppTheBazaar.UI.Tooltips.BaseTooltipController"
                    && access.op.TargetMember?.Contains("FrameSettings", StringComparison.Ordinal)
                        == true
                )
            )
            .ToArray();
        Assert.NotEmpty(art);
        Assert.All(
            art,
            access =>
                Assert.Equal(
                    "BazaarPlusPlus.Android.GameInterop.AndroidNativeArt",
                    access.type.FullName
                )
        );
        // The skin and the portrait moved into the Mobile UI tree (ADR-0025); both must still be
        // here, or the clause below would hold over nothing.
        string[] sharedUi =
        [
            "BazaarPlusPlus.Mobile.Ui.NativeSkin",
            "BazaarPlusPlus.Mobile.Ui.PortraitView",
            "BazaarPlusPlus.Android.GameInterop.AndroidNativeArt",
        ];
        Assert.Equal(sharedUi.Length, host.Types.Count(type => sharedUi.Contains(type.FullName)));
        Assert.DoesNotContain(
            accesses,
            access =>
                sharedUi.Contains(access.type.FullName)
                && access.op.TargetType?.Namespace == "BazaarPlusPlus.Mobile.Collection"
        );
        // The runtime owner never reaches back into the UI, the Mobile UI tree's included.
        Assert.DoesNotContain(
            accesses,
            access =>
                access.type.FullName == "BazaarPlusPlus.Android.GameInterop.AndroidNativeArt"
                && access.op.TargetType?.Namespace is { } ns
                && (
                    ns == "BazaarPlusPlus.Android.Ui"
                    || ns == "BazaarPlusPlus.Mobile"
                    || ns.StartsWith("BazaarPlusPlus.Mobile.", StringComparison.Ordinal)
                )
        );
    }
}
