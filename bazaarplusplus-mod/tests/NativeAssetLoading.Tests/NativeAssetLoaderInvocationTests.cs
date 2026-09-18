#nullable enable
using System.Reflection;
using BazaarPlusPlus.GameInterop.AssetLoading;
using Xunit;

namespace NativeAssetLoading.Tests;

public sealed class NativeAssetLoaderInvocationTests
{
    [Fact]
    public async Task Legacy_single_parameter_signature_is_invokable()
    {
        var method = RequiredMethod(nameof(InstantiateLegacy));
        var assetReference = new FakeAssetReference();

        var supported = NativeAssetLoaderInvocation.TryBuildArguments(
            method,
            assetReference,
            NativeAssetScopeIntent.Current,
            out var arguments
        );

        Assert.True(supported);
        Assert.Single(arguments);
        var task = Assert.IsType<Task<object>>(method.Invoke(null, arguments));
        Assert.Same(assetReference, await task);
    }

    [Fact]
    public async Task Current_scope_intent_uses_native_default()
    {
        var method = RequiredMethod(nameof(InstantiateScoped));
        var assetReference = new FakeAssetReference();

        var supported = NativeAssetLoaderInvocation.TryBuildArguments(
            method,
            assetReference,
            NativeAssetScopeIntent.Current,
            out var arguments
        );

        Assert.True(supported);
        Assert.Equal(2, arguments.Length);
        Assert.Null(arguments[1]);
        var task = Assert.IsType<Task<InvocationResult>>(method.Invoke(null, arguments));
        var result = await task;
        Assert.Same(assetReference, result.AssetReference);
        Assert.Null(result.Scope);
    }

    [Fact]
    public async Task Global_scope_intent_passes_global_enum_value()
    {
        var method = RequiredMethod(nameof(InstantiateScoped));
        var assetReference = new FakeAssetReference();

        var supported = NativeAssetLoaderInvocation.TryBuildArguments(
            method,
            assetReference,
            NativeAssetScopeIntent.Global,
            out var arguments
        );

        Assert.True(supported);
        Assert.Equal(AssetScope.Global, arguments[1]);
        var task = Assert.IsType<Task<InvocationResult>>(method.Invoke(null, arguments));
        Assert.Equal(AssetScope.Global, (await task).Scope);
    }

    [Fact]
    public void Legacy_report_success_flag_is_disabled()
    {
        var method = RequiredMethod(nameof(LoadLegacy));

        var supported = NativeAssetLoaderInvocation.TryBuildArguments(
            method,
            "address",
            NativeAssetScopeIntent.Global,
            out var arguments
        );

        Assert.True(supported);
        Assert.Equal(false, arguments[1]);
    }

    [Fact]
    public void Unknown_second_parameter_is_rejected()
    {
        var method = RequiredMethod(nameof(InstantiateUnknown));

        var supported = NativeAssetLoaderInvocation.TryBuildArguments(
            method,
            new FakeAssetReference(),
            NativeAssetScopeIntent.Current,
            out var arguments
        );

        Assert.False(supported);
        Assert.Empty(arguments);
    }

    [Fact]
    public void Signature_filter_rejects_an_unknown_overload_before_selection()
    {
        var supported = NativeAssetLoaderInvocation.SupportsSignature(
            RequiredMethod(nameof(InstantiateUnknown)),
            typeof(FakeAssetReference)
        );

        Assert.False(supported);
    }

    private static MethodInfo RequiredMethod(string name) =>
        typeof(NativeAssetLoaderInvocationTests).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static
        ) ?? throw new InvalidOperationException($"Missing test method {name}.");

    private static Task<object> InstantiateLegacy(FakeAssetReference assetReference) =>
        Task.FromResult<object>(assetReference);

    private static Task<InvocationResult> InstantiateScoped(
        FakeAssetReference assetReference,
        AssetScope? scope = null
    ) => Task.FromResult(new InvocationResult(assetReference, scope));

    private static Task<object> LoadLegacy(string address, bool reportSuccess = false) =>
        Task.FromResult<object>(address);

    private static Task<object> InstantiateUnknown(
        FakeAssetReference assetReference,
        string? unexpected = null
    ) => Task.FromResult<object>(assetReference);

    private sealed class FakeAssetReference;

    private enum AssetScope
    {
        Global,
        InRun,
        OutOfRun,
    }

    private sealed record InvocationResult(FakeAssetReference AssetReference, AssetScope? Scope);
}
