using System.Text.Json;
using System.Xml.Linq;
using BazaarPlusPlus.TestSupport;
using Xunit;

namespace Architecture.Tests;

public sealed class MacNativeReplayPackagingArchitectureTests
{
    [Fact]
    public void Payload_inventory_owns_native_and_retired_paths()
    {
        using var inventory = JsonDocument.Parse(
            File.OpenRead(Path.Combine(TestInputs.RepoRoot, "..", "release", "payload.json"))
        );
        var files = inventory.RootElement.GetProperty("files").EnumerateArray().ToArray();
        Assert.Contains(
            files,
            entry =>
                entry.GetProperty("producer").GetString() == "native"
                && entry
                    .GetProperty("path")
                    .GetString()!
                    .EndsWith("GfxPluginBppReplayVideoToolbox.bundle", StringComparison.Ordinal)
        );
        Assert.Contains(
            files,
            entry =>
                entry.GetProperty("producer").GetString() == "native"
                && entry
                    .GetProperty("path")
                    .GetString()!
                    .EndsWith("GfxPluginBppReplayMediaFoundation.dll", StringComparison.Ordinal)
        );
        foreach (
            var entry in files.Where(entry =>
                Path.GetFileName(entry.GetProperty("path").GetString()!)
                    .StartsWith("ffmpeg", StringComparison.Ordinal)
            )
        )
        {
            Assert.Equal("retired", entry.GetProperty("producer").GetString());
        }
    }

    [Fact]
    public void Native_artifact_catalog_owns_content_freshness_and_abi_inputs()
    {
        using var catalog = JsonDocument.Parse(
            File.OpenRead(Path.Combine(TestInputs.RepoRoot, "native", "artifacts.json"))
        );
        var platforms = catalog.RootElement.GetProperty("platforms");

        var macos = platforms.GetProperty("macos");
        Assert.Equal("arm64", macos.GetProperty("architecture").GetString());
        Assert.Equal("12.0", macos.GetProperty("deploymentTarget").GetString());
        Assert.Equal("adhoc", macos.GetProperty("signing").GetString());
        var macInputs = macos
            .GetProperty("inputs")
            .EnumerateArray()
            .Select(input => input.GetString())
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("native/mac-audio-tap/build.sh", macInputs);
        Assert.Contains("native/macos/build.sh", macInputs);
        Assert.Contains("native/macos/verify.sh", macInputs);
        Assert.Contains(
            "src/BazaarPlusPlus/Game/CombatReplay/Video/MacMetalVideoEncoder.cs",
            macInputs
        );
        Assert.Contains(
            "src/BazaarPlusPlus/Game/CombatReplay/Audio/CoreAudioProcessTapCaptureTap.cs",
            macInputs
        );
        var macExports = macos
            .GetProperty("artifacts")[1]
            .GetProperty("requiredExports")
            .EnumerateArray()
            .Select(symbol => symbol.GetString())
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("BppVtDiscardRenderEvent", macExports);
        Assert.Contains("UnityPluginLoad", macExports);
        Assert.Contains("UnityPluginUnload", macExports);

        var windows = platforms.GetProperty("windows");
        Assert.Equal("x64", windows.GetProperty("architecture").GetString());
        Assert.Equal("unsigned", windows.GetProperty("signing").GetString());
        var windowsInputs = windows
            .GetProperty("inputs")
            .EnumerateArray()
            .Select(input => input.GetString())
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("native/windows/build.ps1", windowsInputs);
        Assert.Contains("native/windows/test.ps1", windowsInputs);
        Assert.Contains(
            "src/BazaarPlusPlus/Game/CombatReplay/Video/WindowsMediaFoundationVideoEncoder.cs",
            windowsInputs
        );
        var windowsExports = windows
            .GetProperty("artifacts")[0]
            .GetProperty("requiredExports")
            .EnumerateArray()
            .Select(symbol => symbol.GetString())
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("BppMfDiscardRenderEvent", windowsExports);
        Assert.Contains("UnityPluginLoad", windowsExports);
        Assert.Contains("UnityPluginUnload", windowsExports);
    }

    [Fact]
    public void Managed_packaging_uses_the_shared_projection_without_creating_archives()
    {
        var project = XDocument.Load(
            Path.Combine(TestInputs.RepoRoot, "src", "BazaarPlusPlus", "BazaarPlusPlus.csproj")
        );
        Assert.Contains(
            project.Descendants("Import"),
            element =>
                element
                    .Attribute("Project")
                    ?.Value.EndsWith("release/generated/Payload.targets", StringComparison.Ordinal)
                == true
        );
        Assert.Empty(project.Descendants("ZipDirectory"));
        var releaseCopy = Assert.Single(
            project.Descendants("Target"),
            element => element.Attribute("Name")?.Value == "CopyToInstallerSource"
        );
        Assert.Equal(
            "ValidatePayloadAssemblyVersions",
            releaseCopy.Attribute("DependsOnTargets")?.Value
        );
        var copy = Assert.Single(releaseCopy.Descendants("Copy"));
        Assert.Equal("@(BppPayloadFile)", copy.Attribute("SourceFiles")?.Value);
    }
}
