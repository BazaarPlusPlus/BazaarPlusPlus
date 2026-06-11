#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Core.GameState;

internal readonly struct ChoicePedestalSnapshot
{
    public ChoiceScreenPedestalKind Kind { get; init; }
    public IReadOnlyList<string> EnchantmentTypeNames { get; init; }
    public bool IsEnchantChoice => Kind == ChoiceScreenPedestalKind.Enchant;

    public static ChoicePedestalSnapshot Empty { get; } =
        new()
        {
            Kind = ChoiceScreenPedestalKind.None,
            EnchantmentTypeNames = Array.Empty<string>(),
        };
}
