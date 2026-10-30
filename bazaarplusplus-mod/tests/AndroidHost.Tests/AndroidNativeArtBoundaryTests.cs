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
        Assert.DoesNotContain(
            accesses,
            access =>
                access.type.FullName
                    is "BazaarPlusPlus.Android.Ui.AndroidNativeSkin"
                        or "BazaarPlusPlus.Android.Ui.AndroidPortraitView"
                        or "BazaarPlusPlus.Android.GameInterop.AndroidNativeArt"
                && access.op.TargetType?.Namespace == "BazaarPlusPlus.Android.Collection"
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
