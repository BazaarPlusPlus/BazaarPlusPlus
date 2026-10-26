#nullable enable
using System.Collections.Concurrent;
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

    // A loader method's signature is fixed for the process, so its shape is read once per
    // MethodInfo; only the argument array differs per call (the first argument varies).
    private static readonly ConcurrentDictionary<MethodInfo, SignatureShape> Shapes = new();

    internal static bool TryBuildArguments(
        MethodInfo? method,
        object? firstArgument,
        NativeAssetScopeIntent scopeIntent,
        out object?[] arguments
    )
    {
        arguments = Array.Empty<object?>();
        if (method == null || firstArgument == null)
            return false;

        var shape = ShapeOf(method);
        if (!shape.Supports(firstArgument.GetType()))
            return false;

        switch (shape.SecondParameter)
        {
            case SecondParameterKind.None:
                arguments = [firstArgument];
                return true;
            case SecondParameterKind.Bool:
                arguments = [firstArgument, false];
                return true;
            default:
                var scope = scopeIntent switch
                {
                    NativeAssetScopeIntent.Current => null,
                    NativeAssetScopeIntent.Global => shape.GlobalScope
                        ?? Enum.Parse(shape.ScopeType!, GlobalScopeName, ignoreCase: false),
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(scopeIntent),
                        scopeIntent,
                        null
                    ),
                };
                arguments = [firstArgument, scope];
                return true;
        }
    }

    internal static bool SupportsSignature(MethodInfo? method, Type firstArgumentType)
    {
        if (method == null || firstArgumentType == null)
            return false;

        return ShapeOf(method).Supports(firstArgumentType);
    }

    private static SignatureShape ShapeOf(MethodInfo method) =>
        Shapes.GetOrAdd(method, static method => SignatureShape.Read(method));

    private static bool IsOptionalAssetScope(ParameterInfo parameter)
    {
        var scopeType = Nullable.GetUnderlyingType(parameter.ParameterType);
        return parameter.IsOptional
            && scopeType?.IsEnum == true
            && string.Equals(scopeType.Name, AssetScopeTypeName, StringComparison.Ordinal);
    }

    private enum SecondParameterKind
    {
        Unsupported,
        None,
        Bool,
        OptionalAssetScope,
    }

    private sealed class SignatureShape
    {
        private static readonly SignatureShape Unsupported = new(
            null,
            SecondParameterKind.Unsupported,
            null,
            null
        );

        private SignatureShape(
            Type? firstParameterType,
            SecondParameterKind secondParameter,
            Type? scopeType,
            object? globalScope
        )
        {
            FirstParameterType = firstParameterType;
            SecondParameter = secondParameter;
            ScopeType = scopeType;
            GlobalScope = globalScope;
        }

        internal Type? FirstParameterType { get; }

        internal SecondParameterKind SecondParameter { get; }

        internal Type? ScopeType { get; }

        // Boxed once. Null when the enum has no Global member, so the call-time Enum.Parse
        // still throws for a Global request exactly as an uncached read would.
        internal object? GlobalScope { get; }

        internal bool Supports(Type firstArgumentType) =>
            SecondParameter != SecondParameterKind.Unsupported
            && FirstParameterType!.IsAssignableFrom(firstArgumentType);

        internal static SignatureShape Read(MethodInfo method)
        {
            var parameters = method.GetParameters();
            if (parameters.Length is not (1 or 2))
                return Unsupported;

            var first = parameters[0].ParameterType;
            if (parameters.Length == 1)
                return new SignatureShape(first, SecondParameterKind.None, null, null);
            if (parameters[1].ParameterType == typeof(bool))
                return new SignatureShape(first, SecondParameterKind.Bool, null, null);
            if (!IsOptionalAssetScope(parameters[1]))
                return Unsupported;

            var scopeType = Nullable.GetUnderlyingType(parameters[1].ParameterType)!;
            var globalScope = Enum.IsDefined(scopeType, GlobalScopeName)
                ? Enum.Parse(scopeType, GlobalScopeName, ignoreCase: false)
                : null;
            return new SignatureShape(
                first,
                SecondParameterKind.OptionalAssetScope,
                scopeType,
                globalScope
            );
        }
    }
}
