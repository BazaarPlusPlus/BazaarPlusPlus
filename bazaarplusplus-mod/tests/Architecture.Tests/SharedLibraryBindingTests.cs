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
/// Each shared assembly compiles twice from the same sources (ADR-0012, ADR-0013). The Mono
/// target binds the game-supplied libraries in the game's Managed directory (ADR-0010); the
/// net10.0 target binds NuGet packages pinned to the same versions. Both must name the game's
/// version, or one host runs a serializer the other host and the Run segment goldens never saw.
/// </summary>
public sealed class SharedLibraryBindingTests
{
    public static TheoryData<string, string> Bindings =>
        new()
        {
            { CompiledArtifacts.CoreAssembly, "Newtonsoft.Json" },
            { CompiledArtifacts.StorageAssembly, "Newtonsoft.Json" },
            { CompiledArtifacts.ModApiAssembly, "Newtonsoft.Json" },
            { CompiledArtifacts.ModApiAssembly, "MessagePack" },
            { CompiledArtifacts.ModApiAssembly, "MessagePack.Annotations" },
        };

    [Theory]
    [MemberData(nameof(Bindings))]
    public void Both_targets_bind_the_game_assembly_version(string assembly, string library)
    {
        var gameVersion = AssemblyName.GetAssemblyName(GameCopy(library)).Version;

        foreach (var tfm in SharedAssemblies.TargetFrameworks)
        {
            var reference = SharedAssemblies
                .ReferencedAssemblies(assembly, tfm)
                .SingleOrDefault(candidate => candidate.Name == library);
            Assert.True(reference != null, $"{assembly} {tfm} no longer references {library}.");
            Assert.Equal(gameVersion, reference!.Version);
        }
    }

    /// <summary>
    /// Every Newtonsoft.Json 13.x carries assembly version 13.0.0.0, so the binding check above
    /// cannot see net10.0 package drift; the restored package must match the game's file version.
    /// </summary>
    [Theory]
    [MemberData(nameof(Bindings))]
    public void Net10_restores_the_game_file_version(string assembly, string library)
    {
        var gameFile = FileVersionInfo.GetVersionInfo(GameCopy(library));
        var gameVersion =
            $"{gameFile.FileMajorPart}.{gameFile.FileMinorPart}.{gameFile.FileBuildPart}";

        using var lockFile = JsonDocument.Parse(
            TestInputs.Fixture($"src/{assembly}/packages.lock.json")
        );
        var restored = lockFile
            .RootElement.GetProperty("dependencies")
            .GetProperty("net10.0")
            .GetProperty(library)
            .GetProperty("resolved")
            .GetString();

        Assert.Equal(gameVersion, restored);
    }

    private static string GameCopy(string library) =>
        Path.Combine(CompiledArtifacts.Metadata("BppManagedPath"), library + ".dll");
}

/// <summary>Both compiled targets of the shared projects, read as metadata.</summary>
internal static class SharedAssemblies
{
    internal static readonly string[] TargetFrameworks = { "netstandard2.1", "net10.0" };

    internal static IReadOnlyList<AssemblyName> ReferencedAssemblies(
        string assembly,
        string targetFramework
    )
    {
        var path = Path.Combine(
            CompiledArtifacts.Metadata($"BppSharedOutput.{assembly}.{targetFramework}"),
            assembly + ".dll"
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
