#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Cards.Skill;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Resolves a CollectionCardVm into a live, SetUp-ready CardPreviewBase instance vended from
// the pool. Both the Item and Skill branches eventually call CardPreviewBase.SetUp via
// reflection so the mod compiles against any DLL surface where the method exists; the
// instance argument is constructed locally (filling InstanceId / TemplateVersion / Attributes)
// to avoid the NPE paths inside the game's CardPreviewBase.SetUp.
internal sealed class CollectionCardFactory
{
    private readonly CollectionCardPool _pool;
    private readonly Transform _parent;
    private int _instanceCounter;

    public CollectionCardFactory(CollectionCardPool pool, Transform parent)
    {
        _pool = pool;
        _parent = parent;
    }

    public bool ReflectionReady => NativeCardPreviewReflection.SetUpMethod != null;

    public CollectionCardBinding? TryBind(CollectionCardVm vm, Transform? parent = null)
    {
        if (vm == null)
            return null;

        var staticData = BppStaticDataAccess.TryGetReadyManagerObject();
        if (staticData == null)
            return null;

        var template = BppStaticDataAccess.GetCardTemplate(staticData, vm.Id);
        if (template == null)
        {
            BppLog.Warn(
                "CollectionCardFactory",
                $"Template lookup failed for id={vm.Id} ({vm.InternalName})."
            );
            return null;
        }

        var kind =
            vm.Type == ECardType.Skill
                ? NativeCardPreviewKind.ForSkill()
                : NativeCardPreviewKind.ForItem(vm.Size);
        var card = _pool.Take(kind, parent ?? _parent);
        if (card == null)
            return null;

        var instance = BuildSyntheticInstance(vm);
        var setUpTask = NativeCardPreviewRuntime.InvokeSetUpSafe(
            card,
            template,
            instance,
            "CollectionCardFactory"
        );
        return new CollectionCardBinding(card, kind, setUpTask);
    }

    public void Return(Component? card, NativeCardPreviewKind kind) => _pool.Return(card, kind);

    private TCardInstance BuildSyntheticInstance(CollectionCardVm vm)
    {
        var attributes = new Dictionary<ECardAttributeType, int>();
        var id = $"bpp-collection-{++_instanceCounter}";

        if (vm.Type == ECardType.Skill)
        {
            return new TCardInstanceSkill
            {
                TemplateId = vm.Id,
                TemplateVersion = string.Empty,
                InstanceId = id,
                Tier = vm.StartingTier,
                Attributes = attributes,
            };
        }

        return new TCardInstanceItem
        {
            TemplateId = vm.Id,
            TemplateVersion = string.Empty,
            InstanceId = id,
            Tier = vm.StartingTier,
            Attributes = attributes,
        };
    }
}

// One realized card + the bind kind we hand back to the pool on Return + the SetUp task we
// must await before flipping the card visible (to avoid showing a frame mid-LoadArt).
internal readonly struct CollectionCardBinding
{
    public CollectionCardBinding(Component card, NativeCardPreviewKind kind, Task setUpTask)
    {
        Card = card;
        Kind = kind;
        SetUpTask = setUpTask;
    }

    public Component Card { get; }
    public NativeCardPreviewKind Kind { get; }
    public Task SetUpTask { get; }
}
