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

internal sealed class PreviewSkillCardSurface : IPreviewCardSurface
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
                return null;

            if (_staticData == null)
                _staticData = await Data.GetStatic();

            if (_staticData == null)
                return null;

            var card = BuildCard(spec, _staticData);
            if (card == null)
                return null;

            var cardObject = await InstantiateAsync(loader, card, parent.gameObject);
            if (cardObject == null)
                return null;

            if (PreviewCardLifecyclePolicy.ShouldRefreshAfterInstantiate(PreviewCardKind.Skill))
                await RefreshSpawnedSkillAsync(cardObject, card);

            cardObject.AddComponent<ShowcaseCardMarker>();
            ConfigureSpawned(cardObject);
            return cardObject;
        }
        catch (Exception ex)
        {
            BppLog.Error(
                "PreviewSkillCardSurface",
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

        if (cardObject.TryGetComponent<SkillController>(out var skillController))
        {
            skillController.Cleanup();
            skillController.EnableMovement(true);
        }
        else if (cardObject.TryGetComponent<CardController>(out var cardController))
        {
            cardController.EnableMovement(true);
        }

        cardObject.transform.localScale = Vector3.one;
        if (PreviewCardLifecyclePolicy.ShouldReturnToPool(PreviewCardKind.Skill))
            cardObject.PoolObject();
        else
            UnityEngine.Object.Destroy(cardObject);
    }

    private static SkillCard BuildCard(PreviewCardSpec spec, object staticData)
    {
        if (spec == null || string.IsNullOrWhiteSpace(spec.TemplateId))
            return null;

        if (!Guid.TryParse(spec.TemplateId, out var templateId))
            return null;

        var template = GetTemplate(staticData, templateId) as ITCard;
        if (template == null)
            return null;

        var card = new SkillCard
        {
            InstanceId = InstanceId.New("ppskill"),
            TemplateId = templateId,
            Template = template,
            Tier = (ETier)Mathf.Clamp(spec.Tier, 0, 5),
            Type = ECardType.Skill,
            Attributes = new Dictionary<ECardAttributeType, int>(),
            Tags = new HashSet<ECardTag>(),
            HiddenTags = new HashSet<EHiddenTag>(),
            Heroes = new HashSet<EHero>(),
            Owner = null,
            Section = null,
            LeftSocketId = null,
        };

        if (spec.Attributes != null)
        {
            foreach (var kv in spec.Attributes)
            {
                if (Enum.IsDefined(typeof(ECardAttributeType), kv.Key))
                    card.Attributes[(ECardAttributeType)kv.Key] = kv.Value;
            }
        }

        return card;
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
        if (cardObject.TryGetComponent<SkillController>(out var skillController))
        {
            skillController.ShowCard(true);
            skillController.EnableMovement(false);
            return;
        }

        if (cardObject.TryGetComponent<CardController>(out var cardController))
        {
            cardController.ShowCard(true);
            cardController.EnableMovement(false);
        }
    }

    private static async Task RefreshSpawnedSkillAsync(GameObject cardObject, SkillCard card)
    {
        if (cardObject == null || card == null)
            return;

        if (!cardObject.TryGetComponent<SkillController>(out var skillController))
            return;

        skillController.Cleanup();
        await skillController.Setup(card.Template?.ArtKey ?? "Invalid", card, false);
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
            return false;

        var sectionType = _instantiateCardMethod.GetParameters()[2].ParameterType;
        if (!sectionType.IsEnum)
            return false;

        var sectionNames = Enum.GetNames(sectionType);
        if (sectionNames.Contains("Opponent"))
            _spawnSection = Enum.Parse(sectionType, "Opponent");
        else if (sectionNames.Contains("Board"))
            _spawnSection = Enum.Parse(sectionType, "Board");
        else if (sectionNames.Contains("Storage"))
            _spawnSection = Enum.ToObject(sectionType, 0);
        else
            _spawnSection = Enum.ToObject(sectionType, 0);

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
