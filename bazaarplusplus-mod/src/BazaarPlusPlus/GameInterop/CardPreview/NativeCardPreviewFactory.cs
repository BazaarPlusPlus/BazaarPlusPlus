#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Cards.Skill;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal sealed class NativeCardPreviewFactory
{
    private readonly NativeCardPreviewPool _pool;
    private readonly string _logComponent;

    public NativeCardPreviewFactory(NativeCardPreviewPool pool, string logComponent)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _logComponent = string.IsNullOrWhiteSpace(logComponent)
            ? "NativeCardPreviewFactory"
            : logComponent;
    }

    public bool ReflectionReady => NativeCardPreviewReflection.SetUpMethod != null;

    public bool EnsureReady(bool requireSkill = false) => _pool.TryEnsurePrefabRefs(requireSkill);

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

        var card = _pool.Take(kind, parent);
        if (card == null)
            return null;

        var rect = card.transform as RectTransform ?? card.GetComponent<RectTransform>();
        if (rect == null)
        {
            _pool.Return(card, kind);
            return null;
        }

        var instance = BuildSyntheticInstance(spec, kind, instanceIndex);
        var setUpTask = NativeCardPreviewRuntime.InvokeSetUpSafe(
            card,
            template,
            instance,
            _logComponent
        );
        return new NativeCardPreviewHandle(card, rect, kind, setUpTask, spec);
    }

    public void Show(NativeCardPreviewHandle? handle, bool show = true)
    {
        if (handle == null)
            return;
        NativeCardPreviewRuntime.Show(handle.Card, show, _logComponent);
    }

    public void Return(NativeCardPreviewHandle? handle) => _pool.Return(handle);

    public void DestroyAll() => _pool.DestroyAll();

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
