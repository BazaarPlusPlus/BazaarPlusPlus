using System.Reflection;
using Architecture.Tests;
using Xunit;

namespace AndroidHost.Tests;

public sealed class AndroidInteractionOwnershipTests
{
    private const string Runtime = "BazaarPlusPlus.Android.Host.AndroidHostRuntime";
    private const string Lifecycle = "BazaarPlusPlus.Android.Host.AndroidInteractionLifecycle";
    private const string PatchHost = "BazaarPlusPlus.Android.Patches.AndroidPatchHost";

    private static readonly Lazy<CompiledAssembly> Host = new(() =>
        CompiledAssembly.Load(
            typeof(AndroidInteractionOwnershipTests)
                .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "BppAndroidHostAssembly")
                .Value!
        )
    );

    [Theory]
    [InlineData("StartInteraction", "Start")]
    [InlineData("StartHistory", "StartHistory")]
    [InlineData("Frame", "Frame")]
    [InlineData("SceneLoaded", "SceneLoaded")]
    [InlineData("OnPauseChanged", "PauseChanged")]
    [InlineData("Stop", "Stop")]
    public void Runtime_callbacks_use_the_tested_lifecycle(string callback, string operation)
    {
        var method = Host
            .Value.Types.Single(type => type.FullName == Runtime)
            .Methods.Single(method => method.Name == callback);

        Assert.Contains(
            method.Instructions,
            instruction => instruction.Targets(Lifecycle, operation)
        );
    }

    [Theory]
    [InlineData("Publish")]
    [InlineData("Retire")]
    public void Only_the_runtime_changes_the_published_interaction(string operation)
    {
        var owners = Host
            .Value.Types.SelectMany(type => type.Methods)
            .Where(method =>
                method.Instructions.Any(instruction => instruction.Targets(PatchHost, operation))
            )
            .Select(method => method.DeclaringType)
            .ToArray();

        Assert.NotEmpty(owners);
        Assert.All(owners, owner => Assert.StartsWith(Runtime, owner));
    }
}
