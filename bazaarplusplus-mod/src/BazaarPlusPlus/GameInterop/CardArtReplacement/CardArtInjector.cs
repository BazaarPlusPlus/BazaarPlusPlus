#nullable enable
#pragma warning disable CS0436
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using TheBazaar.Game.CardFrames;
using TheBazaar.Utilities.Shaders;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.CardArtReplacement;

internal static class CardArtInjector
{
    private const string LogCategory = "CardArtReplacement";

    private static readonly BindingFlags InstanceFieldFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo? CardIllustrationRendererField =
        typeof(ItemVisualsController).GetField("cardIllustrationRenderer", InstanceFieldFlags);
    private static readonly ConditionalWeakTable<
        ItemVisualsController,
        CardIdentity
    > CardIdentities = new();

    public static void TrackCard(ItemVisualsController visuals, Card card)
    {
        if (visuals == null || card == null)
            return;

        CardIdentities.GetValue(visuals, _ => new CardIdentity()).Card = card;
    }

    public static bool TryResolveCard(ItemVisualsController visuals, out Card? card)
    {
        card = null;
        if (visuals == null)
            return false;

        if (CardIdentities.TryGetValue(visuals, out var identity) && identity.Card != null)
        {
            card = identity.Card;
            return true;
        }

        var controller = visuals.GetComponentInParent<global::ItemController>();
        if (controller != null && controller.CardData != null)
        {
            card = controller.CardData;
            return true;
        }

        return false;
    }

    public static bool IsPackageCard(Card? card)
    {
        if (card == null)
            return false;

        if (PackageIdentity.IsPackage(card.HiddenTags))
            return true;

        if (IsPackageTemplate(card.Template))
            return true;

        try
        {
            var staticData = BppStaticDataAccess.TryGetReadyManagerObject();
            return IsPackageTemplate(
                BppStaticDataAccess.GetCardTemplate(staticData, card.TemplateId)
            );
        }
        catch (Exception ex)
        {
            BppLog.Debug(LogCategory, $"Static package identity lookup failed: {ex.Message}");
            return false;
        }
    }

    public static bool IsPackageTemplate(TCardBase? card) =>
        PackageIdentity.IsPackage(card?.HiddenTags);

    private static bool IsPackageTemplate(ITCard? card) =>
        PackageIdentity.IsPackage(card?.HiddenTags);

    public static bool Apply(ItemVisualsController visuals, Texture2D texture)
    {
        if (visuals == null || texture == null)
            return false;

        var renderer = TryGetCardIllustrationRenderer(visuals);
        if (renderer == null)
            return false;

        return Apply(renderer.sharedMaterial, texture);
    }

    public static bool Apply(Material material, Texture2D texture)
    {
        if (material == null || texture == null)
            return false;

        var appliedBaseMap = false;
        if (material.HasProperty(CardArtShaderVariables.EncounterBaseMap))
        {
            material.SetTexture(CardArtShaderVariables.EncounterBaseMap, texture);
            appliedBaseMap = true;
        }

        material.mainTexture = texture;
        return appliedBaseMap || material.mainTexture == texture;
    }

    private static Renderer? TryGetCardIllustrationRenderer(ItemVisualsController visuals)
    {
        try
        {
            return CardIllustrationRendererField?.GetValue(visuals) as Renderer;
        }
        catch (Exception ex)
        {
            BppLog.Debug(LogCategory, $"Renderer lookup failed: {ex.Message}");
            return null;
        }
    }

    private sealed class CardIdentity
    {
        public Card? Card { get; set; }
    }
}
