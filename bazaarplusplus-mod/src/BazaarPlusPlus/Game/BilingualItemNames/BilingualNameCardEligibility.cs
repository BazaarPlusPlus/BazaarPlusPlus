#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.BilingualItemNames;

internal static class BilingualNameCardEligibility
{
    internal static bool IsSupported(ECardType cardType) =>
        cardType
            is ECardType.Item
                or ECardType.Skill
                or ECardType.CombatEncounter
                or ECardType.EncounterStep
                or ECardType.EventEncounter
                or ECardType.PedestalEncounter;
}
