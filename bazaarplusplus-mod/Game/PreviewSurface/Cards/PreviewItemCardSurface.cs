using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using TheBazaar;
using TheBazaar.AppFramework;
using UnityEngine;

namespace BazaarPlusPlus.Game.PreviewSurface;

internal sealed class PreviewItemCardSurface : IPreviewCardSurface
{
    private MethodInfo _instantiateCardMethod;
    private object _spawnSection;
    private object _staticData;

    public async Task<GameObject> CreateAsync(PreviewCardSpec spec, Transform parent)
    {
        try
        {
            Services.TryGet<AssetLoader>(out var loader);
            if (loader == null || !EnsureApi(loader))
            {
                BppLog.Warn(
                    "PreviewItemCardSurface",
                    $"API unavailable for template={spec?.TemplateId ?? "null"} loader={(loader == null ? "null" : "ok")}"
                );
                return null;
            }

            if (_staticData == null)
            {
                _staticData = await Data.GetStatic();
                BppLog.Debug(
                    "PreviewItemCardSurface",
                    $"Static data loaded={(_staticData != null)}"
                );
            }

            if (_staticData == null)
            {
                BppLog.Warn(
                    "PreviewItemCardSurface",
                    $"Static data unavailable for template={spec?.TemplateId ?? "null"}"
                );
                return null;
            }

            var card = BuildCard(spec, _staticData);
            if (card == null)
            {
                BppLog.Warn(
                    "PreviewItemCardSurface",
                    $"BuildCard failed for template={spec?.TemplateId ?? "null"}"
                );
                return null;
            }

            var cardObject = await InstantiateAsync(loader, card, parent.gameObject);
            if (cardObject == null)
            {
                BppLog.Warn(
                    "PreviewItemCardSurface",
                    $"Instantiate returned null for template={spec?.TemplateId ?? "null"}"
                );
                return null;
            }

            if (PreviewCardLifecyclePolicy.ShouldRefreshAfterInstantiate(PreviewCardKind.Item))
                await RefreshSpawnedItemAsync(cardObject, card);

            cardObject.AddComponent<ShowcaseCardMarker>();
            ConfigureSpawned(cardObject);
            BppLog.Debug(
                "PreviewItemCardSurface",
                $"Created card template={spec?.TemplateId ?? "null"} object={cardObject.name}"
            );
            return cardObject;
        }
        catch (Exception ex)
        {
            BppLog.Error(
                "PreviewItemCardSurface",
                $"CreateAsync failed for template={spec?.TemplateId ?? "null"}",
                ex
            );
            return null;
        }
    }

    public Task UpdateAsync(GameObject cardObject, PreviewCardSpec spec)
    {
        return Task.CompletedTask;
    }

    public void Destroy(GameObject cardObject)
    {
        if (cardObject == null)
            return;

        var marker = cardObject.GetComponent<ShowcaseCardMarker>();
        if (marker != null)
            UnityEngine.Object.Destroy(marker);

        if (cardObject.TryGetComponent<ItemController>(out var itemController))
        {
            itemController.Cleanup();
            itemController.EnableMovement(true);
        }
        else if (cardObject.TryGetComponent<CardController>(out var cardController))
        {
            cardController.EnableMovement(true);
        }

        cardObject.transform.localScale = Vector3.one;
        if (PreviewCardLifecyclePolicy.ShouldReturnToPool(PreviewCardKind.Item))
            cardObject.PoolObject();
        else
            UnityEngine.Object.Destroy(cardObject);
    }

    private static ItemCard BuildCard(PreviewCardSpec entry, object staticData)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.TemplateId))
        {
            BppLog.Warn("PreviewItemCardSurface", "Empty preview card spec");
            return null;
        }

        if (!Guid.TryParse(entry.TemplateId, out var templateId))
        {
            BppLog.Warn("PreviewItemCardSurface", $"Invalid template id: {entry.TemplateId}");
            return null;
        }

        var template = GetTemplate(staticData, templateId) as ITCard;
        if (template == null)
        {
            BppLog.Warn("PreviewItemCardSurface", $"Template not found: {entry.TemplateId}");
            return null;
        }

        var card = new ItemCard
        {
            InstanceId = InstanceId.New("ppmon"),
            TemplateId = templateId,
            Template = template,
            Tier = (ETier)Mathf.Clamp(entry.Tier, 0, 5),
            Size = ParseSize(entry.Size, template.Size),
            Type = ECardType.Item,
            Attributes = new Dictionary<ECardAttributeType, int>(),
            Tags = BuildPreviewTags(template),
            HiddenTags = new HashSet<EHiddenTag>(),
            Heroes = new HashSet<EHero>(),
            Owner = null,
            Section = null,
            LeftSocketId = null,
        };

        if (
            !string.IsNullOrWhiteSpace(entry.Enchant)
            && !string.Equals(entry.Enchant, "None", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse(entry.Enchant, out EEnchantmentType enchantType)
        )
        {
            card.Enchantment = enchantType;
        }

        if (entry.Attributes != null)
        {
            foreach (var kv in entry.Attributes)
            {
                if (Enum.IsDefined(typeof(ECardAttributeType), kv.Key))
                    card.Attributes[(ECardAttributeType)kv.Key] = kv.Value;
            }
        }

        BppLog.Debug(
            "PreviewItemCardSurface",
            $"BuildCard result template={entry.TemplateId} tier={card.Tier} size={card.Size} type={card.Type} enchant={card.Enchantment} attrs={card.Attributes.Count} templateName={template.InternalName}"
        );
        return card;
    }

    private static HashSet<ECardTag> BuildPreviewTags(ITCard template)
    {
        return template?.Tags != null
            ? new HashSet<ECardTag>(template.Tags)
            : new HashSet<ECardTag>();
    }

    private static ECardSize ParseSize(int size, ECardSize fallback)
    {
        switch (size)
        {
            case 1:
                return ECardSize.Small;
            case 2:
                return ECardSize.Medium;
            case 3:
                return ECardSize.Large;
            default:
                return fallback;
        }
    }

    private static object GetTemplate(object staticData, Guid templateId)
    {
        if (staticData == null)
            return null;

        var method = staticData
            .GetType()
            .GetMethod(
                "GetCardById",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Guid) },
                null
            );
        return method?.Invoke(staticData, new object[] { templateId });
    }

    private void ConfigureSpawned(GameObject cardObject)
    {
        if (cardObject.TryGetComponent<ItemController>(out var itemController))
        {
            itemController.ShowCard(true);
            itemController.EnableMovement(false);
            return;
        }

        if (cardObject.TryGetComponent<CardController>(out var cardController))
        {
            cardController.EnableMovement(false);
            cardController.ShowCard(true);
        }
    }

    private static async Task RefreshSpawnedItemAsync(GameObject cardObject, ItemCard card)
    {
        if (cardObject == null || card == null)
            return;

        if (!cardObject.TryGetComponent<ItemController>(out var itemController))
            return;

        itemController.Cleanup();
        ResetItemVfx(itemController);
        await itemController.Setup(card);
        ApplyPreviewStatusVfx(itemController, card);
    }

    private static void ResetItemVfx(ItemController itemController)
    {
        if (itemController == null)
            return;

        InvokeItemVfxMethod(itemController, "DisableAllVFX");
        InvokeItemVfxMethod(itemController, "UpdateHeatedVFX", 0f);
        InvokeItemVfxMethod(itemController, "UpdateChilledVFX", 0f);
        InvokeItemVfxMethod(itemController, "UpdateFreezeVFX", 0f);
    }

    private static void ApplyPreviewStatusVfx(ItemController itemController, ItemCard card)
    {
        if (itemController == null || card?.Attributes == null)
            return;

        var freezeValue = GetPreviewAttributeValue(card, ECardAttributeType.Freeze);
        var heatedValue = GetPreviewAttributeValue(card, ECardAttributeType.Heated);
        var chilledValue = GetPreviewAttributeValue(card, ECardAttributeType.Chilled);

        InvokeItemVfxMethod(itemController, "UpdateFreezeVFX", (float)freezeValue);
        InvokeItemVfxMethod(itemController, "UpdateHeatedVFX", (float)heatedValue);
        InvokeItemVfxMethod(itemController, "UpdateChilledVFX", (float)chilledValue);
    }

    private static int GetPreviewAttributeValue(ItemCard card, ECardAttributeType attributeType)
    {
        if (card?.Attributes == null)
            return 0;

        return card.Attributes.TryGetValue(attributeType, out var value) ? value : 0;
    }

    private static void InvokeItemVfxMethod(
        ItemController itemController,
        string methodName,
        params object[] args
    )
    {
        var cardVfxController = itemController?.CardVFXController;
        if (cardVfxController == null || string.IsNullOrWhiteSpace(methodName))
            return;

        try
        {
            var parameterTypes =
                args?.Select(argument => argument?.GetType() ?? typeof(object)).ToArray()
                ?? Type.EmptyTypes;
            var method = cardVfxController
                .GetType()
                .GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    parameterTypes,
                    null
                );
            method?.Invoke(cardVfxController, args);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "PreviewItemCardSurface",
                $"Failed to invoke item VFX method '{methodName}': {ex.Message}"
            );
        }
    }

    private bool EnsureApi(AssetLoader loader)
    {
        if (_instantiateCardMethod != null && _spawnSection != null)
            return true;

        _instantiateCardMethod = loader
            .GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(method =>
            {
                if (!string.Equals(method.Name, "InstantiateCardAsync", StringComparison.Ordinal))
                    return false;

                var parameters = method.GetParameters();
                return parameters.Length == 3
                    && parameters[0].ParameterType == typeof(Card)
                    && parameters[1].ParameterType == typeof(GameObject)
                    && parameters[2].ParameterType.IsEnum;
            });

        if (_instantiateCardMethod == null)
        {
            BppLog.Warn("PreviewItemCardSurface", "InstantiateCardAsync API not found");
            return false;
        }

        var sectionType = _instantiateCardMethod.GetParameters()[2].ParameterType;
        if (!sectionType.IsEnum)
        {
            BppLog.Warn("PreviewItemCardSurface", "Spawn section parameter is not enum");
            return false;
        }

        var sectionNames = Enum.GetNames(sectionType);
        if (sectionNames.Contains("Opponent"))
            _spawnSection = Enum.Parse(sectionType, "Opponent");
        else if (sectionNames.Contains("Board"))
            _spawnSection = Enum.Parse(sectionType, "Board");
        else if (sectionNames.Contains("Storage"))
            _spawnSection = Enum.Parse(sectionType, "Storage");
        else
            _spawnSection = Enum.ToObject(sectionType, 0);
        BppLog.Debug(
            "PreviewItemCardSurface",
            $"Resolved instantiate API with spawnSection={_spawnSection}"
        );
        return _spawnSection != null;
    }

    private async Task<GameObject> InstantiateAsync(
        AssetLoader loader,
        Card card,
        GameObject parent
    )
    {
        if (_instantiateCardMethod == null || _spawnSection == null)
            return null;

        var taskObject = _instantiateCardMethod.Invoke(
            loader,
            new object[] { card, parent, _spawnSection }
        );
        if (taskObject is Task<GameObject> typedTask)
            return await typedTask;

        if (taskObject is Task task)
        {
            await task;
            return task.GetType().GetProperty("Result")?.GetValue(task) as GameObject;
        }

        return null;
    }
}
