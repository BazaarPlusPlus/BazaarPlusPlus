#nullable enable
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using BazaarPlusPlus.TestSupport;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// BazaarPlusPlus.Core is shared by every host (ADR-0012), so neither of its compiled targets
/// may bind a host, loader, engine, or game assembly; its one host-switched binding is
/// Newtonsoft.Json, which on the Mono target must be the game's own copy (ADR-0010).
/// </summary>
public sealed class CoreAssemblyBoundaryTests
{
    private static readonly string[] TargetFrameworks = { "netstandard2.1", "net10.0" };

    private static readonly string[] ForbiddenPrefixes =
    {
        "UnityEngine",
        "Unity.",
        "BepInEx",
        "0Harmony",
        "MelonLoader",
        "Il2Cpp",
        "Assembly-CSharp",
        "TheBazaar",
        "BazaarGame",
        "BazaarBattleService",
        "MessagePack",
    };

    [Fact]
    public void Core_targets_reference_no_host_loader_engine_or_game_assembly()
    {
        var violations = TargetFrameworks
            .SelectMany(tfm =>
                ReferencedAssemblies(tfm)
                    .Where(reference =>
                        reference.Name == CompiledArtifacts.MainAssembly
                        || ForbiddenPrefixes.Any(prefix =>
                            reference.Name!.StartsWith(prefix, StringComparison.Ordinal)
                        )
                    )
                    .Select(reference => $"{tfm}: {reference.FullName}")
            )
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "BazaarPlusPlus.Core must stay host-independent:\n  " + string.Join("\n  ", violations)
        );
    }

    [Fact]
    public void Core_binds_the_game_version_of_Newtonsoft_Json_on_both_targets()
    {
        var gameCopy = Path.Combine(
            CompiledArtifacts.Metadata("BppManagedPath"),
            "Newtonsoft.Json.dll"
        );
        var gameVersion = AssemblyName.GetAssemblyName(gameCopy).Version;

        foreach (var tfm in TargetFrameworks)
        {
            var newtonsoft = ReferencedAssemblies(tfm)
                .SingleOrDefault(reference => reference.Name == "Newtonsoft.Json");
            Assert.True(newtonsoft != null, $"{tfm}: Core no longer references Newtonsoft.Json.");
            Assert.Equal(gameVersion, newtonsoft!.Version);
        }
    }

    /// <summary>
    /// Every Newtonsoft.Json 13.x carries assembly version 13.0.0.0, so the binding check above
    /// cannot see net10.0 package drift; the restored package must match the game's file version.
    /// </summary>
    [Fact]
    public void Core_net10_restores_the_game_file_version_of_Newtonsoft_Json()
    {
        var gameFile = FileVersionInfo.GetVersionInfo(
            Path.Combine(CompiledArtifacts.Metadata("BppManagedPath"), "Newtonsoft.Json.dll")
        );
        var gameVersion =
            $"{gameFile.FileMajorPart}.{gameFile.FileMinorPart}.{gameFile.FileBuildPart}";

        using var lockFile = JsonDocument.Parse(
            TestInputs.Fixture("src/BazaarPlusPlus.Core/packages.lock.json")
        );
        var restored = lockFile
            .RootElement.GetProperty("dependencies")
            .GetProperty("net10.0")
            .GetProperty("Newtonsoft.Json")
            .GetProperty("resolved")
            .GetString();

        Assert.Equal(gameVersion, restored);
    }

    private static IReadOnlyList<AssemblyName> ReferencedAssemblies(string targetFramework)
    {
        var path = Path.Combine(
            CompiledArtifacts.Metadata("BppCoreOutput." + targetFramework),
            CompiledArtifacts.CoreAssembly + ".dll"
        );
        Assert.True(File.Exists(path), $"Compiled artifact '{path}' is missing.");

        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        return reader
            .AssemblyReferences.Select(handle =>
                reader.GetAssemblyReference(handle).GetAssemblyName()
            )
            .ToArray();
    }
}
