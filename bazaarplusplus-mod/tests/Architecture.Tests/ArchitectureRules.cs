#nullable enable
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Assertion vocabulary over <see cref="CompiledArtifacts"/>. A rule names its scope and its
/// forbidden targets; both must resolve (ADR-0009), so a renamed type fails the rule instead of
/// silently passing it.
/// </summary>
internal static class ArchitectureRules
{
    /// <summary>Evaluates the rule against every scanned configuration.</summary>
    internal static void Holds(string rule, Func<CompiledBuild, IEnumerable<string>> violations)
    {
        var all = CompiledArtifacts
            .Builds.SelectMany(build =>
                violations(build).Select(violation => $"[{build.Configuration}] {violation}")
            )
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(all.Length == 0, rule + "\n  " + string.Join("\n  ", all));
    }

    internal static IEnumerable<string> References(
        IEnumerable<CompiledType> scope,
        Func<TypeName, bool> forbidden
    ) =>
        scope.SelectMany(type =>
            type.References.Where(forbidden).Select(target => $"{type} references {target}")
        );

    internal static IEnumerable<string> References(
        CompiledType scope,
        Func<TypeName, bool> forbidden
    ) => References(new[] { scope }, forbidden);

    /// <summary>Calls, field accesses, and constructions of the named members.</summary>
    internal static IEnumerable<string> Accesses(
        IEnumerable<CompiledMethod> scope,
        params (string Type, string Member)[] members
    ) =>
        scope.SelectMany(method =>
            method
                .Instructions.Where(op =>
                    op.IsMemberAccess
                    && members.Any(member => op.Targets(member.Type, member.Member))
                )
                .Select(op => $"{method} {op}")
        );

    internal static IEnumerable<string> Accesses(
        IEnumerable<CompiledType> scope,
        params (string Type, string Member)[] members
    ) => Accesses(scope.SelectMany(type => type.Methods), members);

    internal static IEnumerable<string> Accesses(
        CompiledType scope,
        params (string Type, string Member)[] members
    ) => Accesses(scope.Methods, members);

    /// <summary>
    /// Whole-word occurrences of <paramref name="words"/> in string literals, which is how a
    /// reflection-by-name or SQL reach would surface.
    /// </summary>
    internal static IEnumerable<string> Literals(
        IEnumerable<CompiledMethod> scope,
        StringComparison comparison,
        params string[] words
    ) =>
        scope.SelectMany(method =>
            method
                .Literals.SelectMany(literal =>
                    words.Where(word => ContainsWord(literal, word, comparison))
                )
                .Select(word => $"{method} ldstr contains '{word}'")
        );

    internal static IEnumerable<string> Literals(
        IEnumerable<CompiledType> scope,
        StringComparison comparison,
        params string[] words
    ) => Literals(scope.SelectMany(type => type.Methods), comparison, words);

    internal static Func<TypeName, bool> Is(params string[] fullNames) =>
        type => fullNames.Contains(type.FullName, StringComparer.Ordinal);

    internal static Func<TypeName, bool> InNamespace(params string[] namespaces) =>
        type => namespaces.Any(ns => CompiledBuild.IsInNamespace(type.Namespace, ns));

    internal static Func<TypeName, bool> FromAssembly(params string[] prefixes) =>
        type => prefixes.Any(prefix => type.Assembly.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>Fails unless the build references some assembly whose name has this prefix.</summary>
    internal static string RequireAssemblyPrefix(CompiledBuild build, string prefix)
    {
        if (!build.Types.SelectMany(type => type.References).Any(FromAssembly(prefix)))
            throw new InvalidOperationException(
                $"{build.Configuration}: rule target assembly prefix '{prefix}' does not resolve."
            );
        return prefix;
    }

    /// <summary>Fails unless some type in the build references <paramref name="fullName"/>.</summary>
    internal static string RequireReferencedType(CompiledBuild build, string fullName)
    {
        if (!build.Types.SelectMany(type => type.References).Any(Is(fullName)))
            throw new InvalidOperationException(
                $"{build.Configuration}: rule target type '{fullName}' does not resolve."
            );
        return fullName;
    }

    internal static bool ContainsWord(string text, string word, StringComparison comparison)
    {
        for (
            var index = text.IndexOf(word, comparison);
            index >= 0;
            index = text.IndexOf(word, index + 1, comparison)
        )
        {
            // Word boundaries apply only at edges that are themselves word characters, so
            // "/run-bundles" still matches inside "/v5/run-bundles".
            var end = index + word.Length;
            var startsClean = !IsWordChar(word[0]) || index == 0 || !IsWordChar(text[index - 1]);
            var endsClean = !IsWordChar(word[^1]) || end == text.Length || !IsWordChar(text[end]);
            if (startsClean && endsClean)
                return true;
        }

        return false;
    }

    private static bool IsWordChar(char value) => char.IsLetterOrDigit(value) || value == '_';
}
