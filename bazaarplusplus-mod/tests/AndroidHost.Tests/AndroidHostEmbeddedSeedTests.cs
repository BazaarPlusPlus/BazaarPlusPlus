#nullable enable
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Game.Supporters;
using Xunit;

namespace AndroidHost.Tests;

public sealed class AndroidHostEmbeddedSeedTests
{
    public static TheoryData<string> SeedNames =>
        new()
        {
            TenWinBuildCatalogSettings.EmbeddedResourceName,
            SupporterCatalogSettings.EmbeddedResourceName,
        };

    [Theory]
    [MemberData(nameof(SeedNames))]
    public void The_android_host_embeds_each_catalog_seed_under_the_core_name(string name)
    {
        var path = typeof(AndroidHostEmbeddedSeedTests)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "BppAndroidHostAssembly")
            .Value;
        Assert.True(File.Exists(path), $"The Android host was not built at '{path}'.");
        using var pe = new PEReader(File.OpenRead(path!));
        var metadata = pe.GetMetadataReader();
        var resources = metadata
            .ManifestResources.Select(handle =>
                metadata.GetString(metadata.GetManifestResource(handle).Name)
            )
            .ToArray();

        Assert.Contains(name, resources);
    }
}
