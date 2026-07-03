#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Cards.Skill;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using TheBazaar.AppFramework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal sealed class NativeCardPreviewFactory
{
    private readonly int _layer;
    private readonly string _logComponent;
    private readonly List<GameObject> _created = new();

    public NativeCardPreviewFactory(int layer, string logComponent)
    {
        _layer = layer;
        _logComponent = string.IsNullOrWhiteSpace(logComponent)
            ? "NativeCardPreviewFactory"
            : logComponent;
    }

    public bool ReflectionReady => NativeCardPreviewReflection.CardPreviewBaseType != null;

    public bool EnsureReady(bool requireSkill = false) =>
        NativeCardPreviewReflection.CardPreviewBaseType != null
        && Services.TryGet<AssetLoader>(out var assetLoader)
        && assetLoader != null;

    public bool TryResolveSpan(NativeCardPreviewSpec? spec, out int span)
    {
        span = 1;
        if (!TryResolveTemplate(spec, out var template))
            return false;

        span = CardSizeSpan.Resolve(template.Size);
        return true;
    }

    public NativeCardPreviewHandle? TryCreate(
        NativeCardPreviewSpec? spec,
        Transform parent,
        int instanceIndex
    )
    {
        if (parent == null || !TryResolveTemplate(spec, out var template) || spec == null)
            return null;

        if (!TryResolveKind(template, out var kind))
        {
            BppLog.Warn(
                _logComponent,
                $"Unsupported card preview type={template.Type} size={template.Size} template={template.Id}."
            );
            return null;
        }

        var instance = BuildSyntheticInstance(spec, kind, instanceIndex);
        var handle = new NativeCardPreviewHandle(kind, spec);
        _ = CreateCardAsync(handle, parent, instance, kind, instanceIndex);
        return handle;

        async Task CreateCardAsync(
            NativeCardPreviewHandle previewHandle,
            Transform handleParent,
            TCardInstance cardInstance,
            NativeCardPreviewKind previewKind,
            int index
        )
        {
            try
            {
                if (!Services.TryGet<AssetLoader>(out var assetLoader) || assetLoader == null)
                {
                    BppLog.Warn(_logComponent, "AssetLoader unavailable for native card preview.");
                    previewHandle.MarkReady();
                    return;
                }

                var go = await assetLoader.InstantiateUICardAsync(
                    cardInstance,
                    handleParent,
                    CancellationToken.None
                );
                if (go == null)
                {
                    previewHandle.MarkReady();
                    return;
                }

                if (previewHandle.IsReleased)
                {
                    Object.Destroy(go);
                    previewHandle.MarkReady();
                    return;
                }

                go.name = $"BppNativeCardPreview_{previewKind}_{Mathf.Max(0, index)}";
                NativeCardPreviewReflection.ApplyLayerRecursive(go, _layer);
                _created.Add(go);

                var card = go.GetComponent(NativeCardPreviewReflection.CardPreviewBaseType!);
                var rect = go.transform as RectTransform ?? go.GetComponent<RectTransform>();
                if (card == null || rect == null)
                {
                    BppLog.Warn(
                        _logComponent,
                        $"Instantiated UI card without CardPreviewBase/RectTransform template={cardInstance.TemplateId}."
                    );
                    Object.Destroy(go);
                    _created.Remove(go);
                    previewHandle.MarkReady();
                    return;
                }

                previewHandle.Bind(card, rect);
                go.SetActive(true);
                NativeCardPreviewRuntime.Resize(card, _logComponent);
                previewHandle.MarkReady();
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    _logComponent,
                    $"InstantiateUICardAsync failed for template={cardInstance.TemplateId}: {ex.Message}"
                );
                previewHandle.MarkFailed(ex);
                return;
            }
        }
    }

    public void Show(NativeCardPreviewHandle? handle, bool show = true)
    {
        if (handle?.Card == null)
            return;
        NativeCardPreviewRuntime.Show(handle.Card, show, _logComponent);
    }

    public void Return(NativeCardPreviewHandle? handle)
    {
        if (handle == null)
            return;

        handle.MarkReleased();
        if (handle.Card == null)
            return;

        var go = handle.Card.gameObject;
        if (go != null)
        {
            _created.Remove(go);
            Object.Destroy(go);
        }
    }

    public void DestroyAll()
    {
        foreach (var go in _created)
        {
            if (go != null)
                Object.Destroy(go);
        }
        _created.Clear();
    }

    private bool TryResolveTemplate(NativeCardPreviewSpec? spec, out TCardBase template)
    {
        template = null!;
        if (spec == null || spec.TemplateId == Guid.Empty)
            return false;

        var staticData = BppStaticDataAccess.TryGetReadyManagerObject();
        if (staticData == null)
        {
            BppLog.Debug(_logComponent, "Static data unavailable for native card preview.");
            return false;
        }

        var resolved = BppStaticDataAccess.GetCardTemplate(staticData, spec.TemplateId);
        if (resolved == null)
        {
            BppLog.Warn(_logComponent, $"Template lookup failed for id={spec.TemplateId}.");
            return false;
        }

        template = resolved;
        return true;
    }

    private static bool TryResolveKind(TCardBase template, out NativeCardPreviewKind kind)
    {
        kind = default;
        if (template.Type == ECardType.Skill)
        {
            kind = NativeCardPreviewKind.ForSkill();
            return true;
        }

        if (template.Type == ECardType.Item)
        {
            kind = NativeCardPreviewKind.ForItem(ResolveCardSize(template.Size));
            return true;
        }

        return false;
    }

    private static TCardInstance BuildSyntheticInstance(
        NativeCardPreviewSpec spec,
        NativeCardPreviewKind kind,
        int index
    )
    {
        var attributes =
            spec.Attributes != null
                ? new Dictionary<ECardAttributeType, int>(spec.Attributes)
                : new Dictionary<ECardAttributeType, int>();
        var instanceId = $"{spec.InstanceIdPrefix}-{Mathf.Max(0, index)}";

        if (kind.Type == ECardType.Skill)
        {
            return new TCardInstanceSkill
            {
                TemplateId = spec.TemplateId,
                TemplateVersion = string.Empty,
                InstanceId = instanceId,
                Tier = spec.Tier,
                Attributes = attributes,
            };
        }

        return new TCardInstanceItem
        {
            TemplateId = spec.TemplateId,
            TemplateVersion = string.Empty,
            InstanceId = instanceId,
            Tier = spec.Tier,
            SocketId = spec.SocketId ?? (EContainerSocketId)Mathf.Clamp(index, 0, 9),
            EnchantmentType = spec.EnchantmentType,
            Attributes = attributes,
        };
    }

    private static ECardSize ResolveCardSize(ECardSize size) =>
        size switch
        {
            ECardSize.Small => ECardSize.Small,
            ECardSize.Medium => ECardSize.Medium,
            ECardSize.Large => ECardSize.Large,
            _ => ECardSize.Small,
        };
}
