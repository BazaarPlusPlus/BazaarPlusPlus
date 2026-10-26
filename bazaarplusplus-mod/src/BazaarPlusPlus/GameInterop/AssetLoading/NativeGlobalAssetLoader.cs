#nullable enable
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using UnityEngine.AddressableAssets;

namespace BazaarPlusPlus.GameInterop.AssetLoading;

internal static class NativeGlobalAssetLoader
{
    private const string LoadByAddressMethodName = "LoadAssetAsyncByAddress";
    private const string LoadByReferenceMethodName = "LoadAssetAsyncByReference";
    private const string InRunScopeName = "InRun";

    private static readonly MethodInfo? LoadByAddressMethod = ResolveGenericMethod(
        LoadByAddressMethodName,
        typeof(string)
    );
    private static readonly MethodInfo? LoadByReferenceMethod = ResolveGenericMethod(
        LoadByReferenceMethodName,
        typeof(AssetReference)
    );
    private static readonly PropertyInfo? CurrentScopeProperty = typeof(AssetLoader).GetProperty(
        "CurrentScope",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
    );

    // Global by default, for process-static caches that must outlive the lobby <-> run scope
    // release. A load that serves only the present scene passes Current, so the game releases
    // it with that scope.
    internal static Task<T?> LoadByAddressAsync<T>(
        AssetLoader assetLoader,
        string address,
        NativeAssetScopeIntent scope = NativeAssetScopeIntent.Global
    )
        where T : UnityEngine.Object =>
        InvokeAsync<T>(assetLoader, LoadByAddressMethod, LoadByAddressMethodName, address, scope);

    internal static Task<T?> LoadByReferenceAsync<T>(
        AssetLoader assetLoader,
        AssetReference assetReference,
        NativeAssetScopeIntent scope = NativeAssetScopeIntent.Global
    )
        where T : UnityEngine.Object =>
        InvokeAsync<T>(
            assetLoader,
            LoadByReferenceMethod,
            LoadByReferenceMethodName,
            assetReference,
            scope
        );

    internal static bool CanRunNativeSharedPreload(AssetLoader assetLoader)
    {
        if (CurrentScopeProperty == null)
            return true;

        return string.Equals(
            CurrentScopeProperty.GetValue(assetLoader)?.ToString(),
            InRunScopeName,
            StringComparison.Ordinal
        );
    }

    private static async Task<T?> InvokeAsync<T>(
        AssetLoader assetLoader,
        MethodInfo? method,
        string methodName,
        object firstArgument,
        NativeAssetScopeIntent scope
    )
        where T : UnityEngine.Object
    {
        if (assetLoader == null || firstArgument == null)
            return null;
        if (firstArgument is AssetReference assetReference && !assetReference.RuntimeKeyIsValid())
            return null;
        if (firstArgument is string address && string.IsNullOrWhiteSpace(address))
            return null;
        if (method == null)
            throw new MissingMethodException(typeof(AssetLoader).FullName, methodName);

        if (
            !NativeAssetLoaderInvocation.TryBuildArguments(
                method,
                firstArgument,
                scope,
                out var arguments
            )
        )
        {
            throw new MissingMethodException(typeof(AssetLoader).FullName, methodName);
        }

        try
        {
            var raw = ClosedMethods<T>.For(method).Invoke(assetLoader, arguments);
            if (raw is not Task<T> task)
            {
                throw new InvalidOperationException(
                    $"{typeof(AssetLoader).FullName}.{methodName} returned an unexpected task type."
                );
            }

            return await task;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    // One closed generic per (open method, T); a failed close throws per call, as before.
    private static class ClosedMethods<T>
    {
        private static readonly ConcurrentDictionary<MethodInfo, MethodInfo> Methods = new();

        internal static MethodInfo For(MethodInfo method) =>
            Methods.GetOrAdd(method, static open => open.MakeGenericMethod(typeof(T)));
    }

    private static MethodInfo? ResolveGenericMethod(string methodName, Type firstParameterType) =>
        typeof(AssetLoader)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method =>
                method.Name == methodName
                && method.IsGenericMethodDefinition
                && method.GetGenericArguments().Length == 1
            )
            .Where(method =>
                NativeAssetLoaderInvocation.SupportsSignature(method, firstParameterType)
            )
            .FirstOrDefault();
}
