#nullable enable
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using BazaarPlusPlus.TestSupport;
using Xunit;

namespace Architecture.Tests;

// Pin both targets' serializer bindings so neither host runs a version the Run segment goldens never exercised.
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

    // Newtonsoft.Json 13.x shares one assembly version, so check the restored package against the game's file version.
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
