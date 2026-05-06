#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Players;
using BazaarGameShared.Domain.Values;
using TheBazaar;
using TheBazaar.AppFramework;
using TheBazaar.Localization;
using TheBazaar.Tooltips;

namespace BazaarPlusPlus.Game.Tooltips;

internal static class CardTooltipDataFactory
{
    // Runtime ctor binding is not stable across the game's shipped assemblies,
    // so refresh paths clone tooltip data by populating the private fields the
    // constructor normally initializes.
    private static readonly FieldInfo CardInstanceField = typeof(CardTooltipData).GetField(
        "_cardInstance",
        BindingFlags.Instance | BindingFlags.NonPublic
    )!;

    private static readonly FieldInfo CardTemplateField = typeof(CardTooltipData).GetField(
        "_cardTemplate",
        BindingFlags.Instance | BindingFlags.NonPublic
    )!;

    private static readonly FieldInfo MonsterField = typeof(CardTooltipData).GetField(
        "_monster",
        BindingFlags.Instance | BindingFlags.NonPublic
    )!;

    private static readonly FieldInfo ValueContextField = typeof(CardTooltipData).GetField(
        "_valueContext",
        BindingFlags.Instance | BindingFlags.NonPublic
    )!;

    private static readonly FieldInfo CompiledTooltipsField = typeof(CardTooltipData).GetField(
        "_compiledTooltips",
        BindingFlags.Instance | BindingFlags.NonPublic
    )!;

    private static readonly FieldInfo LocalizationServiceField = typeof(CardTooltipData).GetField(
        "_localizationService",
        BindingFlags.Instance | BindingFlags.NonPublic
    )!;

    internal static CardTooltipData Create(Card card, CardTooltipData source)
    {
        return Create(card, source, MonsterField.GetValue(source) as TMonster);
    }

    internal static TMonster? GetMonster(CardTooltipData tooltipData)
    {
        return tooltipData != null ? MonsterField.GetValue(tooltipData) as TMonster : null;
    }

    internal static CardTooltipData Create(Card card, CardTooltipData source, TMonster? monster)
    {
        if (card == null)
            throw new ArgumentNullException(nameof(card));
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        var tooltipData = (CardTooltipData)
            RuntimeHelpers.GetUninitializedObject(typeof(CardTooltipData));

        CardInstanceField.SetValue(tooltipData, card);
        CardTemplateField.SetValue(tooltipData, source.CardTemplate);
        MonsterField.SetValue(tooltipData, monster);
        ValueContextField.SetValue(tooltipData, new ValueContext(Data.Run, card));
        CompiledTooltipsField.SetValue(tooltipData, CreateCompiledTooltipsCache());
        LocalizationServiceField.SetValue(
            tooltipData,
            LocalizationServiceField.GetValue(source) ?? Services.Get<LocalizationService>()
        );
        tooltipData.CanFuse = source.CanFuse;

        return tooltipData;
    }

    private static object CreateCompiledTooltipsCache()
    {
        return Activator.CreateInstance(CompiledTooltipsField.FieldType)
            ?? throw new InvalidOperationException(
                $"Failed to create compiled tooltip cache of type '{CompiledTooltipsField.FieldType.FullName}'."
            );
    }
}
