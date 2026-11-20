#nullable enable
namespace Architecture.Tests;

// Static readonly fields construct colours in the type initializer. Color.white and Color.clear are getters.
internal static class InGameColourRule
{
    internal const string ThemeTypeName = "InGameTheme";

    private static readonly (string Type, string Member)[] Forbidden =
    {
        ("UnityEngine.Color", ".ctor"),
        ("UnityEngine.Color32", ".ctor"),
        ("UnityEngine.ColorUtility", "TryParseHtmlString"),
    };

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

    // Require a matching construction in the theme so a renamed colour type cannot make the rule pass vacuously.
    internal static void RequireThemeConstructsColours(CompiledType theme)
    {
        if (!theme.Instructions.Any(op => op.Targets(Forbidden[0].Type, Forbidden[0].Member)))
            throw new InvalidOperationException(
                $"{theme.FullName} constructs no {Forbidden[0].Type}; the colour rule targets nothing."
            );
    }
}
