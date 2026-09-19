using BazaarPlusPlus.TestSupport;
using Xunit;

namespace Architecture.Tests;

public sealed class MacNativeReplayNativeArchitectureTests
{
    [Fact]
    public void Native_source_owns_video_and_audio_finalize_without_release_credentials()
    {
        var nativeRoot = Path.Combine(TestInputs.RepoRoot, "native", "macos");
        var header = File.ReadAllText(Path.Combine(nativeRoot, "BppReplayVideoToolbox.h"));
        var build = File.ReadAllText(Path.Combine(nativeRoot, "build.sh"));

        Assert.Contains("BppVtPrepareRenderEvent", header, StringComparison.Ordinal);
        Assert.Contains("BppVtCommitRenderEvent", header, StringComparison.Ordinal);
        Assert.Contains("BppVtDiscardRenderEvent", header, StringComparison.Ordinal);
        Assert.Contains("BppVtMuxAudio", header, StringComparison.Ordinal);
        Assert.Contains("-framework VideoToolbox", build, StringComparison.Ordinal);
        Assert.Contains("-framework AVFoundation", build, StringComparison.Ordinal);
        Assert.Contains("codesign --force --sign -", build, StringComparison.Ordinal);
        Assert.DoesNotContain("Developer ID", build, StringComparison.Ordinal);
        Assert.DoesNotContain("notary", build, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mac_native_builds_target_the_game_macos_12_floor()
    {
        var replayBuild = File.ReadAllText(
            Path.Combine(TestInputs.RepoRoot, "native", "macos", "build.sh")
        );
        var audioBuild = File.ReadAllText(
            Path.Combine(TestInputs.RepoRoot, "native", "mac-audio-tap", "build.sh")
        );

        foreach (var build in new[] { replayBuild, audioBuild })
        {
            Assert.Contains("-mmacosx-version-min=12.0", build, StringComparison.Ordinal);
            Assert.DoesNotContain("-mmacosx-version-min=11.0", build, StringComparison.Ordinal);
            Assert.Contains("xcrun --sdk macosx", build, StringComparison.Ordinal);
            Assert.Contains("-isysroot", build, StringComparison.Ordinal);
            Assert.Contains("-Werror=unguarded-availability", build, StringComparison.Ordinal);
        }
        Assert.Contains(
            "-install_name @rpath/libBppMacAudio.dylib",
            audioBuild,
            StringComparison.Ordinal
        );

        var verifier = File.ReadAllText(
            Path.Combine(TestInputs.RepoRoot, "native", "macos", "verify.sh")
        );
        Assert.Contains("vtool -show-build", verifier, StringComparison.Ordinal);
        Assert.Contains("lipo -archs", verifier, StringComparison.Ordinal);
        Assert.Contains("weak external", verifier, StringComparison.Ordinal);
        Assert.Contains("codesign --verify --strict", verifier, StringComparison.Ordinal);
    }

    [Fact]
    public void Windows_native_source_requires_d3d11_hardware_mft_and_tracked_surfaces()
    {
        var nativeRoot = Path.Combine(TestInputs.RepoRoot, "native", "windows");
        var header = File.ReadAllText(Path.Combine(nativeRoot, "BppReplayMediaFoundation.h"));
        var source = File.ReadAllText(Path.Combine(nativeRoot, "BppReplayMediaFoundation.cpp"));
        var normalizedSource = source.ReplaceLineEndings("\n");
        var build = File.ReadAllText(Path.Combine(nativeRoot, "build.ps1"));
        var smokeTest = File.ReadAllText(Path.Combine(nativeRoot, "test.ps1"));

        Assert.Contains("BppMfPrepareRenderEvent", header, StringComparison.Ordinal);
        Assert.Contains("BppMfDiscardRenderEvent", header, StringComparison.Ordinal);
        Assert.Contains("BppMfMuxAudio", header, StringComparison.Ordinal);
        Assert.Contains("MFCreateDXGISurfaceBuffer", source, StringComparison.Ordinal);
        Assert.Contains("IMFTrackedSample", source, StringComparison.Ordinal);
        Assert.Contains("MFT_ENUM_FLAG_HARDWARE", source, StringComparison.Ordinal);
        Assert.Contains("MFT_ENUM_HARDWARE_URL_Attribute", source, StringComparison.Ordinal);
        Assert.Contains("category != MFT_CATEGORY_VIDEO_ENCODER", source, StringComparison.Ordinal);
        Assert.Contains("TransformOutputsH264", source, StringComparison.Ordinal);
        Assert.Contains("firstFrameIndex + index", source, StringComparison.Ordinal);
        Assert.DoesNotContain("nextFrameIndex", source, StringComparison.Ordinal);
        Assert.Contains("previousMultithreadProtection", source, StringComparison.Ordinal);
        Assert.Contains("destroyRequested", source, StringComparison.Ordinal);
        Assert.Contains("MF_READWRITE_D3D_OPTIONAL, FALSE", source, StringComparison.Ordinal);
        Assert.Contains(
            "outputColor.Nominal_Range = D3D11_VIDEO_PROCESSOR_NOMINAL_RANGE_16_235;",
            source,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("outputColor.RGB_Range", source, StringComparison.Ordinal);
        Assert.Contains(
            "case DXGI_FORMAT_B8G8R8A8_UNORM_SRGB:\n        processorSourceFormat = DXGI_FORMAT_B8G8R8A8_UNORM;",
            normalizedSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "case DXGI_FORMAT_R8G8B8A8_UNORM_SRGB:\n        processorSourceFormat = DXGI_FORMAT_R8G8B8A8_UNORM;",
            normalizedSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("ffmpeg", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/W4 /WX", build, StringComparison.Ordinal);
        Assert.Contains("/utf-8", build, StringComparison.Ordinal);
        Assert.Contains("/utf-8", smokeTest, StringComparison.Ordinal);
        Assert.Contains("/Brepro", build, StringComparison.Ordinal);
        Assert.Contains("dumpbin /headers", build, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dumpbin /exports", build, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dumpbin /dependents", build, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Join-Path $PSHOME 'Modules\\Microsoft.PowerShell.Security\\Microsoft.PowerShell.Security.psd1'",
            build,
            StringComparison.Ordinal
        );
        Assert.Contains("Import-Module -Name $securityModulePath -ErrorAction Stop", build);
        Assert.Contains("Get-AuthenticodeSignature", build, StringComparison.Ordinal);
        Assert.Contains("test.ps1", build, StringComparison.Ordinal);
    }

    [Fact]
    public void Mac_native_writer_shutdown_and_metal_pipeline_are_serialized()
    {
        var source = File.ReadAllText(
            Path.Combine(TestInputs.RepoRoot, "native", "macos", "BppReplayVideoToolbox.mm")
        );

        Assert.Contains("CancelWriterOnQueue", source, StringComparison.Ordinal);
        Assert.Contains("writerClosed", source, StringComparison.Ordinal);
        Assert.Contains("gConversionPipelines", source, StringComparison.Ordinal);
        Assert.Contains("objectForKey:device", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Native_encoder_bounds_videotoolbox_delay_without_making_it_a_hard_requirement()
    {
        var source = File.ReadAllText(
            Path.Combine(TestInputs.RepoRoot, "native", "macos", "BppReplayVideoToolbox.mm")
        );

        Assert.Contains("int maxFrameDelayCount = 2;", source, StringComparison.Ordinal);
        Assert.Contains(
            "kVTCompressionPropertyKey_MaxFrameDelayCount,\n                maxFrameDelayCountNumber",
            source,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "SetCompressionProperty(\n                encoder,\n                kVTCompressionPropertyKey_MaxFrameDelayCount",
            source,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Render_event_packet_ownership_is_documented_and_asymmetric_on_both_backends()
    {
        // The compiler cannot tell Cancel from Discard: both take the same opaque pointer and
        // differ only in how many references they consume. Discard on a queued event is a
        // use-after-free, Cancel on an unqueued one leaks, so the contract is pinned here.
        // Both headers carry the same contract; neither may drift away from it alone.
        foreach (
            var headerPath in new[]
            {
                Path.Combine(TestInputs.RepoRoot, "native", "macos", "BppReplayVideoToolbox.h"),
                Path.Combine(
                    TestInputs.RepoRoot,
                    "native",
                    "windows",
                    "BppReplayMediaFoundation.h"
                ),
            }
        )
        {
            var header = File.ReadAllText(headerPath);
            Assert.Contains("releases the caller's reference", header, StringComparison.Ordinal);
            Assert.Contains("releases BOTH references", header, StringComparison.Ordinal);
            Assert.Contains("event that WAS queued", header, StringComparison.Ordinal);
        }

        var macSource = File.ReadAllText(
                Path.Combine(TestInputs.RepoRoot, "native", "macos", "BppReplayVideoToolbox.mm")
            )
            .ReplaceLineEndings("\n");
        Assert.Contains(
            "        CompleteRenderEvent(packet->encoder);\n    }\n    ReleaseRenderEventPacket(packet);",
            macSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "        CompleteRenderEvent(packet->encoder);\n        ReleaseRenderEventPacket(packet);\n    }\n    ReleaseRenderEventPacket(packet);",
            macSource,
            StringComparison.Ordinal
        );

        var windowsSource = File.ReadAllText(
                Path.Combine(
                    TestInputs.RepoRoot,
                    "native",
                    "windows",
                    "BppReplayMediaFoundation.cpp"
                )
            )
            .ReplaceLineEndings("\n");
        Assert.Contains(
            "        CompleteRenderEvent(packet->encoder);\n    }\n    ReleasePacket(packet);",
            windowsSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "        CompleteRenderEvent(packet->encoder);\n        ReleasePacket(packet);\n    }\n    ReleasePacket(packet);",
            windowsSource,
            StringComparison.Ordinal
        );

        var session = File.ReadAllText(
                Path.Combine(
                    TestInputs.RepoRoot,
                    "src",
                    "BazaarPlusPlus",
                    "Game",
                    "CombatReplay",
                    "Video",
                    "ReplayVideoCaptureSession.cs"
                )
            )
            .ReplaceLineEndings("\n");
        // Count rather than Contains: the session has more than one submission path, and a
        // single correct branch must not vouch for an inverted sibling.
        var queuedBranches = session.Split("if (eventQueued)").Length - 1;
        var correctBranches =
            session
                .Split(
                    "if (eventQueued)\n                encoder.CancelRenderEvent(eventData);\n            else\n                encoder.DiscardRenderEvent(eventData);"
                )
                .Length - 1;
        Assert.True(queuedBranches > 0, "No queued-event abandon branch found in the session.");
        Assert.Equal(queuedBranches, correctBranches);
    }
}
