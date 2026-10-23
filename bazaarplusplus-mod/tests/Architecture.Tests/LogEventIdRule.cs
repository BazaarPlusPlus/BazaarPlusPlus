#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace Architecture.Tests;

/// <summary>
/// The id rule <c>BppLogEventRenderer</c> enforces at runtime, checked over compiled call sites
/// (ADR-0009) so a bad id fails a test instead of rendering as <c>logging.render.failed</c>. Each
/// <c>BppLogEvent</c> construction passes a declared scope's getter and, directly after it, a
/// literal id under that scope's prefix with at least three dotted-snake segments
/// (<see cref="BppLogSchemaRules.IsDottedSnakeIdentifier"/>). Compiled into Architecture.Tests,
/// which scans the desktop and shared assemblies, and AndroidHost.Tests, which scans the Android
/// host.
/// </summary>
internal static class LogEventIdRule
{
    internal const string EventType = "BazaarPlusPlus.Infrastructure.Logging.BppLogEvent";
    internal const string ScopeType = "BazaarPlusPlus.Infrastructure.Logging.BppLogFeatureScope";
    internal const int MinimumSegments = 3;

    /// <summary>
    /// Scope property name to event id prefix, read from the scope type's static constructor:
    /// <c>ldstr prefixName; ldstr eventIdPrefix; newobj .ctor; stsfld &lt;Name&gt;k__BackingField</c>.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> Prefixes(CompiledType scopeType)
    {
        var prefixes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var method in scopeType.Methods.Where(method => method.Name == ".cctor"))
        {
            string? last = null;
            string? constructed = null;
            foreach (var op in method.Instructions)
            {
                if (op.Literal != null)
                    last = op.Literal;
                else if (op.OpCode == "newobj" && op.Targets(ScopeType, ".ctor"))
                    constructed = last;
                else if (
                    op.OpCode == "stsfld"
                    && constructed != null
                    && op.TargetMember is { } field
                    && field.StartsWith('<')
                    && field.EndsWith(">k__BackingField", StringComparison.Ordinal)
                )
                {
                    prefixes[field[1..field.IndexOf('>')]] = constructed;
                    constructed = null;
                }
            }
        }

        if (prefixes.Count == 0)
            throw new InvalidOperationException(
                $"Rule scope '{ScopeType}' declares no scopes in its static constructor."
            );
        return prefixes;
    }

    /// <summary>Every <c>BppLogEvent</c> construction in <paramref name="methods"/>.</summary>
    internal static IReadOnlyList<(CompiledMethod Method, string? Scope, string? Id)> Constructions(
        IEnumerable<CompiledMethod> methods,
        IReadOnlyDictionary<string, string> prefixes
    )
    {
        var found = new List<(CompiledMethod, string?, string?)>();
        foreach (var method in methods)
        {
            string? scope = null;
            string? id = null;
            var expectId = false;
            foreach (var op in method.Instructions)
            {
                if (
                    op.OpCode == "call"
                    && op.TargetType?.FullName == ScopeType
                    && op.TargetMember is { } getter
                    && getter.StartsWith("get_", StringComparison.Ordinal)
                    && prefixes.ContainsKey(getter[4..])
                )
                {
                    scope = getter[4..];
                    id = null;
                    expectId = true;
                    continue;
                }

                // The id is the literal recorded right after the getter; storm keys come later.
                if (expectId && op.Literal != null)
                    id = op.Literal;
                expectId = false;
                if (op.OpCode is "newobj" or "call" && op.Targets(EventType, ".ctor"))
                {
                    found.Add((method, scope, id));
                    scope = null;
                    id = null;
                }
            }
        }

        return found;
    }

    /// <summary>One line per construction whose id would not render; fails if none is found.</summary>
    internal static IEnumerable<string> Violations(
        IEnumerable<CompiledMethod> methods,
        IReadOnlyDictionary<string, string> prefixes
    )
    {
        var constructions = Constructions(methods, prefixes);
        if (constructions.Count == 0)
            throw new InvalidOperationException(
                $"Rule scope constructs no '{EventType}'; the scan no longer matches the build."
            );

        foreach (var (method, scope, id) in constructions)
        {
            if (scope == null || id == null)
                yield return $"{method} constructs a BppLogEvent without a declared scope and literal id";
            else if (!BppLogSchemaRules.IsDottedSnakeIdentifier(id, MinimumSegments))
                yield return $"{method} \"{id}\" is not {MinimumSegments}+ dotted-snake segments";
            else if (!id.StartsWith(prefixes[scope] + ".", StringComparison.Ordinal))
                yield return $"{method} \"{id}\" is outside scope {scope} ({prefixes[scope]}.*)";
        }
    }
}
