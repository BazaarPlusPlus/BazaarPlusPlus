#nullable enable
using System.Reflection;

namespace BazaarPlusPlus.GameInterop.AssetLoading;

internal enum NativeAssetScopeIntent
{
    Current,
    Global,
}

internal static class NativeAssetLoaderInvocation
{
    private const string AssetScopeTypeName = "AssetScope";
    private const string GlobalScopeName = "Global";

    internal static bool TryBuildArguments(
        MethodInfo? method,
        object? firstArgument,
        NativeAssetScopeIntent scopeIntent,
        out object?[] arguments
    )
    {
        arguments = Array.Empty<object?>();
        if (
            method == null
            || firstArgument == null
            || !SupportsSignature(method, firstArgument.GetType())
        )
        {
            return false;
        }

        var parameters = method.GetParameters();
        if (parameters.Length == 1)
        {
            arguments = [firstArgument];
            return true;
        }

        if (parameters[1].ParameterType == typeof(bool))
        {
            arguments = [firstArgument, false];
            return true;
        }

        if (!IsOptionalAssetScope(parameters[1]))
            return false;

        var scopeType = Nullable.GetUnderlyingType(parameters[1].ParameterType)!;

        var scope = scopeIntent switch
        {
            NativeAssetScopeIntent.Current => null,
            NativeAssetScopeIntent.Global => Enum.Parse(
                scopeType,
                GlobalScopeName,
                ignoreCase: false
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(scopeIntent), scopeIntent, null),
        };
        arguments = [firstArgument, scope];
        return true;
    }

    internal static bool SupportsSignature(MethodInfo? method, Type firstArgumentType)
    {
        if (method == null || firstArgumentType == null)
            return false;

        var parameters = method.GetParameters();
        if (
            parameters.Length is not (1 or 2)
            || !parameters[0].ParameterType.IsAssignableFrom(firstArgumentType)
        )
        {
            return false;
        }

        return parameters.Length == 1
            || parameters[1].ParameterType == typeof(bool)
            || IsOptionalAssetScope(parameters[1]);
    }

    private static bool IsOptionalAssetScope(ParameterInfo parameter)
    {
        var scopeType = Nullable.GetUnderlyingType(parameter.ParameterType);
        return parameter.IsOptional
            && scopeType?.IsEnum == true
            && string.Equals(scopeType.Name, AssetScopeTypeName, StringComparison.Ordinal);
    }
}
