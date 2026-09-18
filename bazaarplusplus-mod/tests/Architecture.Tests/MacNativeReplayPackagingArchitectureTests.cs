using System.Text.Json;
using System.Xml.Linq;
using BazaarPlusPlus.TestSupport;
using Xunit;

namespace Architecture.Tests;

public sealed class MacNativeReplayPackagingArchitectureTests
{
    [Fact]
    public void Desktop_payloads_have_no_ffmpeg_and_ship_native_render_plugins()
    {
        var projectPath = Path.Combine(
            TestInputs.RepoRoot,
            "src",
            "BazaarPlusPlus",
            "BazaarPlusPlus.csproj"
        );
        var project = XDocument.Load(projectPath);
        var elements = project.Descendants().ToList();

        Assert.DoesNotContain(elements, element => element.Name.LocalName == "MacFfmpegZip");
        Assert.DoesNotContain(elements, element => element.Name.LocalName == "MacFfmpegLicense");
        Assert.DoesNotContain(elements, element => element.Name.LocalName == "WindowsFfmpegZip");
        Assert.DoesNotContain(
            elements,
            element => element.Name.LocalName == "WindowsFfmpegLicense"
        );
        Assert.Contains(elements, element => element.Name.LocalName == "WindowsReplayPlugin");
        Assert.Contains(elements, element => element.Name.LocalName == "IsWindowsHost");
        Assert.Contains(
            elements,
            element =>
                element.Name.LocalName == "Copy"
                && (element.Attribute("DestinationFiles")?.Value ?? string.Empty).Contains(
                    "TheBazaar_Data\\Plugins\\x86_64\\GfxPluginBppReplayMediaFoundation.dll",
                    StringComparison.Ordinal
                )
        );

        var unzips = elements.Where(element => element.Name.LocalName == "Unzip").ToList();
        Assert.DoesNotContain(
            unzips,
            element =>
                (element.Attribute("DestinationFolder")?.Value ?? string.Empty).Contains(
                    "SourceForBuild/macos",
                    StringComparison.Ordinal
                )
        );
        Assert.DoesNotContain(
            unzips,
            element =>
                (element.Attribute("DestinationFolder")?.Value ?? string.Empty).Contains(
                    "SourceForBuild/windows",
                    StringComparison.Ordinal
                )
        );

        var staleFiles = elements
            .Where(element => element.Name.LocalName == "StaleRecorderFile")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains(
            "$(BPPInstallerSourcePath)/SourceForBuild/macos/BepInEx/plugins/ffmpeg",
            staleFiles
        );
        Assert.Contains(
            "$(BPPInstallerSourcePath)/SourceForBuild/macos/BepInEx/plugins/ffmpeg-LICENSE.txt",
            staleFiles
        );

        Assert.Contains(
            staleFiles,
            path =>
                path.EndsWith("BepInEx/plugins/ffmpeg.exe", StringComparison.Ordinal)
                || path.EndsWith("BepInEx\\plugins\\ffmpeg.exe", StringComparison.Ordinal)
        );
        Assert.Contains(
            staleFiles,
            path => path.EndsWith("ffmpeg-LICENSE.txt", StringComparison.Ordinal)
        );

        var packageErrors = elements
            .Where(element => element.Name.LocalName == "Error")
            .Select(element => element.Attribute("Condition")?.Value ?? string.Empty)
            .ToList();
        Assert.Contains(
            packageErrors,
            condition =>
                condition.Contains("InstallerMacReplayPluginBundle", StringComparison.Ordinal)
        );
        Assert.Contains(
            packageErrors,
            condition =>
                condition.Contains("InstallerWindowsReplayPlugin", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Native_artifact_catalog_owns_content_freshness_and_abi_inputs()
    {
        using var catalog = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(TestInputs.RepoRoot, "native", "artifacts.json"))
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
    public void Publish_ensures_native_inputs_before_managed_packaging()
    {
        var runScript = File.ReadAllText(Path.Combine(TestInputs.RepoRoot, "run.sh"));
        var publishStart = runScript.IndexOf("publish() {", StringComparison.Ordinal);
        var ensure = runScript.IndexOf(
            "ensure_native_release_inputs \"$installer_source\" \"$release_platform\"",
            publishStart,
            StringComparison.Ordinal
        );
        var remoteData = runScript.IndexOf(
            "fetch_remote_data \"${common_args[@]}\"",
            publishStart,
            StringComparison.Ordinal
        );
        Assert.True(ensure > publishStart);
        Assert.True(remoteData > ensure);
        var managedBuild = runScript.IndexOf(
            "dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj",
            remoteData,
            StringComparison.Ordinal
        );
        var prepareArchives = runScript.IndexOf(
            "prepare_installer_resource_archives \"$installer_source\" \"$release_platform\"",
            managedBuild,
            StringComparison.Ordinal
        );
        Assert.True(managedBuild > remoteData);
        Assert.True(prepareArchives > managedBuild);
        Assert.Contains(
            "\"-p:BppReleasePlatform=$release_platform\"",
            runScript,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "run prepare:resources -- --platform \"$release_platform\"",
            runScript,
            StringComparison.Ordinal
        );

        var project = XDocument.Load(
            Path.Combine(TestInputs.RepoRoot, "src", "BazaarPlusPlus", "BazaarPlusPlus.csproj")
        );
        var releaseCopy = project
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Target"
                && element.Attribute("Name")?.Value == "CopyToInstallerSource"
            );
        Assert.DoesNotContain("LocalMacReplayPluginBundle", releaseCopy.ToString());
        Assert.DoesNotContain("LocalWindowsReplayPlugin", releaseCopy.ToString());
        Assert.Contains("$(BppReleasePlatform)", releaseCopy.ToString());

        var releasePackage = project
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Target"
                && element.Attribute("Name")?.Value == "PackageInstallerSource"
            );
        var zip = Assert.Single(
            releasePackage.Descendants(),
            element => element.Name.LocalName == "ZipDirectory"
        );
        Assert.Contains("$(BppReleasePlatform)", zip.ToString());

        var releaseValidation = project
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Target"
                && element.Attribute("Name")?.Value == "ValidateProductionReleasePlatform"
            );
        Assert.Contains("macos", releaseValidation.ToString(), StringComparison.Ordinal);
        Assert.Contains("windows", releaseValidation.ToString(), StringComparison.Ordinal);
    }
}
