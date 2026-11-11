#nullable enable
namespace Architecture.Tests;

/// <summary>
/// The in-game kit's colour rule (ADR-0026): a panel draws only the <c>ingame-*</c> tokens of its
/// generated <c>InGameTheme</c>, and a fraction of one goes through <c>InGameTheme.WithAlpha</c>,
/// so no panel constructs a <c>Color</c> or <c>Color32</c> or parses a colour string. A
/// <c>static readonly</c> colour is a construction in the type's static constructor, so it is
/// caught too; <c>Color.white</c> and <c>Color.clear</c> are getters, not constructions. Compiled
/// into Architecture.Tests, which scans the Desktop UI, and AndroidHost.Tests, which scans the
/// Mobile UI the Android host compiles.
/// </summary>
internal static class InGameColourRule
{
    internal const string ThemeTypeName = "InGameTheme";

    private static readonly (string Type, string Member)[] Forbidden =
    {
        ("UnityEngine.Color", ".ctor"),
        ("UnityEngine.Color32", ".ctor"),
        ("UnityEngine.ColorUtility", "TryParseHtmlString"),
    };

    /// <summary>
    /// Every forbidden construction or parse in <paramref name="scope"/>, except in the generated
    /// theme.
    /// </summary>
    internal static IEnumerable<string> Violations(IEnumerable<CompiledType> scope) =>
        scope
            .Where(type => type.Name != ThemeTypeName)
            .SelectMany(type => type.Methods)
            .SelectMany(method =>
                method
                    .Instructions.Where(op =>
                        op.IsMemberAccess
                        && Forbidden.Any(member => op.Targets(member.Type, member.Member))
                    )
                    .Select(op => $"{method} {op}")
            );

    /// <summary>
    /// Fails unless the generated theme constructs its colours, so a renamed colour type cannot
    /// make <see cref="Violations"/> pass over nothing.
    /// </summary>
    internal static void RequireThemeConstructsColours(CompiledType theme)
    {
        if (!theme.Instructions.Any(op => op.Targets(Forbidden[0].Type, Forbidden[0].Member)))
            throw new InvalidOperationException(
                $"{theme.FullName} constructs no {Forbidden[0].Type}; the colour rule targets nothing."
            );
    }
}
