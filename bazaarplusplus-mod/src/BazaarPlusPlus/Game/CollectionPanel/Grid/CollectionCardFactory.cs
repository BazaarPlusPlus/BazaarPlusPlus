#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Cards.Skill;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using TheBazaar.AppFramework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Resolves a CollectionCardVm into a native UI card using the game's current AssetLoader
// path. The collection grid is virtualized, so bindings may be returned before their async
// InstantiateUICardAsync call completes; the binding owns that race and destroys late cards.
internal sealed class CollectionCardFactory
{
    private readonly RectTransform _parent;
    private readonly int _layer;
    private readonly CollectionCardPool _pool = new();
    private readonly GameObject _stagingRoot;
    private readonly HashSet<CollectionCardBinding> _bindings = new();
    private bool _disposed;
    private int _instanceCounter;

    public CollectionCardFactory(RectTransform parent, int layer)
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _layer = layer;

        _stagingRoot = new GameObject(
            "CollectionPanelCardStaging",
            typeof(RectTransform),
            typeof(CanvasGroup)
        );
        _stagingRoot.transform.SetParent(parent, worldPositionStays: false);
        NativeCardPreviewReflection.ApplyLayerRecursive(_stagingRoot, layer);

        var canvasGroup = _stagingRoot.GetComponent<CanvasGroup>();
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }

    public bool ReflectionReady => NativeCardPreviewReflection.CardPreviewBaseType != null;
    public CollectionCardFactoryStats Stats { get; } = new();

    public CollectionCardBinding? TryBind(CollectionCardVm vm)
    {
        if (vm == null)
            return null;

        if (_disposed)
            return null;

        if (!Services.TryGet<AssetLoader>(out var assetLoader) || assetLoader == null)
            return null;

        var kind =
            vm.Type == ECardType.Skill
                ? NativeCardPreviewKind.ForSkill()
                : NativeCardPreviewKind.ForItem(vm.Size);

        var instanceIndex = ++_instanceCounter;
        var instance = BuildSyntheticInstance(vm, instanceIndex);
        var binding = TakeBinding(kind, instanceIndex);
        binding.BeginBind(instanceIndex);
        _bindings.Add(binding);
        if (binding.Card == null)
            _ = CreateCardAsync(binding, assetLoader, instance, kind, instanceIndex);
        else
            _ = RebindCardAsync(binding, instance, instanceIndex);
        return binding;
    }

    public void Return(CollectionCardBinding? binding)
    {
        if (binding == null)
            return;

        binding.MarkReleased();
        _bindings.Remove(binding);
        if (binding.SetUpTask.IsCompleted)
            ReturnReadyBinding(binding);
        else
        {
            Stats.RecordPendingReturn();
            binding.MarkPendingReturn();
        }
    }

    public void DestroyAll()
    {
        _disposed = true;
        foreach (var binding in _bindings)
        {
            binding.MarkReleased();
            DestroyBindingObjects(binding);
        }
        _bindings.Clear();
        _pool.DestroyAll();

        if (_stagingRoot != null)
            Object.Destroy(_stagingRoot);
    }

    private CollectionCardBinding TakeBinding(NativeCardPreviewKind kind, int instanceIndex)
    {
        if (_pool.TryTake(kind, out var binding))
        {
            Stats.RecordPoolReuse();
            binding.Reattach(_parent, instanceIndex);
            return binding;
        }

        Stats.RecordColdCreate();
        return new CollectionCardBinding(kind, _parent, _layer, instanceIndex);
    }

    private async Task CreateCardAsync(
        CollectionCardBinding binding,
        AssetLoader assetLoader,
        TCardInstance instance,
        NativeCardPreviewKind kind,
        int instanceIndex
    )
    {
        GameObject? go = null;
        try
        {
            if (_disposed || _stagingRoot == null)
            {
                binding.MarkReady();
                DestroyBindingObjects(binding);
                return;
            }

            var cardParent = binding.Socket != null ? binding.Socket : _stagingRoot.transform;
            go = await assetLoader.InstantiateUICardAsync(instance, cardParent, CancellationToken.None);
            if (go == null)
            {
                binding.MarkFailed(
                    new InvalidOperationException(
                        $"AssetLoader returned null for collection template={instance.TemplateId}."
                    )
                );
                return;
            }

            if (_disposed || binding.IsReleased)
            {
                Object.Destroy(go);
                binding.MarkReady();
                DestroyBindingObjects(binding);
                return;
            }

            ResetCardVisualState(binding);
            go.name = $"CollectionPanelCard_{kind}_{Mathf.Max(0, instanceIndex)}";
            NativeCardPreviewReflection.ApplyLayerRecursive(go, _layer);

            var card = go.GetComponent(NativeCardPreviewReflection.CardPreviewBaseType!);
            var rect = go.transform as RectTransform ?? go.GetComponent<RectTransform>();
            if (card == null || rect == null)
            {
                BppLog.Warn(
                    "CollectionCardFactory",
                    $"Instantiated collection card without CardPreviewBase/RectTransform template={instance.TemplateId}."
                );
                Object.Destroy(go);
                binding.MarkFailed(
                    new InvalidOperationException(
                        $"Instantiated collection card without CardPreviewBase/RectTransform template={instance.TemplateId}."
                    )
                );
                DestroyBindingObjects(binding);
                return;
            }

            if (go.GetComponent<CollectionPanelOwnedMarker>() == null)
                go.AddComponent<CollectionPanelOwnedMarker>();

            go.SetActive(true);
            NativeCardPreviewRuntime.Resize(card, "CollectionCardFactory");

            binding.Bind(card, rect);
            await RebindCardAsync(binding, instance, instanceIndex, forceSetUp: false);
            if (binding.IsPendingReturn || binding.IsReleased)
                ReturnReadyBinding(binding);
        }
        catch (Exception ex)
        {
            if (go != null)
                Object.Destroy(go);
            DestroyBindingObjects(binding);
            BppLog.Warn(
                "CollectionCardFactory",
                $"InstantiateUICardAsync failed for collection template={instance.TemplateId}: {ex.Message}"
            );
            binding.MarkFailed(ex);
        }
    }

    private async Task RebindCardAsync(
        CollectionCardBinding binding,
        TCardInstance instance,
        int instanceIndex,
        bool forceSetUp = true
    )
    {
        try
        {
            if (_disposed || binding.Card == null)
            {
                binding.MarkReady();
                return;
            }

            ResetCardVisualState(binding);
            if (forceSetUp)
            {
                var staticData = BppStaticDataAccess.TryGetReadyManagerObject();
                var template =
                    staticData != null
                        ? BppStaticDataAccess.GetCardTemplate(staticData, instance.TemplateId)
                        : null;
                if (template == null)
                    throw new InvalidOperationException(
                        $"Template lookup failed for pooled collection template={instance.TemplateId}."
                    );

                await NativeCardPreviewRuntime.InvokeSetUpSafe(
                    binding.Card,
                    template,
                    instance,
                    "CollectionCardFactory"
                );
                NativeCardPreviewRuntime.Resize(binding.Card, "CollectionCardFactory");
            }

            binding.MarkMetricsDirty();
            binding.MarkReady();
            if (binding.IsPendingReturn || binding.IsReleased)
                ReturnReadyBinding(binding);
        }
        catch (Exception ex)
        {
            Stats.RecordRebindFault();
            BppLog.Warn(
                "CollectionCardFactory",
                $"Collection card rebind failed for template={instance.TemplateId}: {ex.Message}"
            );
            binding.MarkFailed(ex);
            if (binding.IsPendingReturn || binding.IsReleased)
                DestroyBindingObjects(binding);
        }
    }

    private void ReturnReadyBinding(CollectionCardBinding binding)
    {
        if (_disposed || binding.IsDestroyed)
            return;

        if (binding.Card == null || binding.SetUpTask.IsFaulted || binding.SetUpTask.IsCanceled)
        {
            DestroyBindingObjects(binding);
            return;
        }

        binding.PrepareForPool();
        _pool.Return(binding);
    }

    private void DestroyBindingObjects(CollectionCardBinding binding)
    {
        var go = binding.Card?.gameObject;
        if (binding.Host != null)
            binding.DestroyHost();
        else if (go != null)
            Object.Destroy(go);
    }

    private TCardInstance BuildSyntheticInstance(CollectionCardVm vm, int instanceIndex)
    {
        var attributes = new Dictionary<ECardAttributeType, int>();
        var id = $"bpp-collection-{instanceIndex}";

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

    private static void ResetCardVisualState(CollectionCardBinding binding)
    {
        var host = binding.Host;
        if (host != null)
        {
            host.localScale = Vector3.one;
            host.localRotation = Quaternion.identity;
            var hostGroup = host.GetComponent<CanvasGroup>();
            if (hostGroup != null)
                hostGroup.alpha = 0f;
        }

        var card = binding.Card;
        if (card == null)
            return;

        card.gameObject.SetActive(true);
        NativeCardPreviewRuntime.Show(card, show: false, logComponent: "CollectionCardFactory");

        var badge = card.gameObject.transform.Find("BppCollectionSourceAttributionBadge");
        if (badge != null)
            badge.gameObject.SetActive(false);
    }
}

// One realized collection card request. Card/Rect are populated only after the game's
// AssetLoader has created and bound the native UI card.
internal sealed class CollectionCardBinding
{
    private TaskCompletionSource<object?> _ready = NewReadySource();
    private int _bindGeneration;

    public CollectionCardBinding(
        NativeCardPreviewKind kind,
        RectTransform parent,
        int layer,
        int instanceIndex
    )
    {
        Kind = kind;
        Host = CreateHost(parent, layer, instanceIndex);
        CanvasGroup = Host.gameObject.AddComponent<CanvasGroup>();
        CanvasGroup.alpha = 0f;
        CanvasGroup.interactable = false;
        CanvasGroup.blocksRaycasts = false;
        Socket = ItemBoardSocketLayout.BuildSocket(
            Host,
            layer,
            $"CollectionPanelNativeSocket_{Mathf.Max(0, instanceIndex)}"
        );
        Socket.anchoredPosition = Vector2.zero;
        Host.sizeDelta = Socket.sizeDelta;
    }

    public Component? Card { get; private set; }
    public RectTransform? Rect { get; private set; }
    public RectTransform? Host { get; private set; }
    public RectTransform? Socket { get; private set; }
    public RectTransform? Frame { get; private set; }
    public CanvasGroup? CanvasGroup { get; private set; }
    public CollectionCardFrameMetrics? FrameMetrics { get; private set; }
    public NativeCardPreviewKind Kind { get; }
    public Task SetUpTask => _ready.Task;
    public bool IsReleased { get; private set; }
    public bool IsPendingReturn { get; private set; }
    public bool IsDestroyed { get; private set; }
    public int BindGeneration => _bindGeneration;

    public void Bind(Component card, RectTransform rect)
    {
        Card = card ?? throw new ArgumentNullException(nameof(card));
        Rect = rect ?? throw new ArgumentNullException(nameof(rect));
        Frame = FindDescendant(rect, "FrameContainer") ?? rect;
    }

    public void MarkReady() => _ready.TrySetResult(null);

    public void MarkFailed(Exception ex) => _ready.TrySetException(ex);

    public void MarkReleased() => IsReleased = true;
    public void MarkPendingReturn() => IsPendingReturn = true;

    public void BeginBind(int instanceIndex)
    {
        _bindGeneration++;
        _ready = NewReadySource();
        IsReleased = false;
        IsPendingReturn = false;
        MarkMetricsDirty();
        if (Host != null)
        {
            Host.name = $"CollectionPanelCardHost_{Mathf.Max(0, instanceIndex)}";
            Host.gameObject.SetActive(true);
        }
        if (Socket != null)
            Socket.name = $"CollectionPanelNativeSocket_{Mathf.Max(0, instanceIndex)}";
        if (CanvasGroup != null)
            CanvasGroup.alpha = 0f;
    }

    public void Reattach(RectTransform parent, int instanceIndex)
    {
        if (Host == null)
            return;
        Host.SetParent(parent, worldPositionStays: false);
        Host.gameObject.SetActive(true);
        Host.localScale = Vector3.one;
        Host.localRotation = Quaternion.identity;
        Host.anchoredPosition = Vector2.zero;
        Host.name = $"CollectionPanelCardHost_{Mathf.Max(0, instanceIndex)}";
        if (Socket != null)
            Socket.anchoredPosition = Vector2.zero;
    }

    public void PrepareForPool()
    {
        IsReleased = false;
        IsPendingReturn = false;
        HoverRelay?.Clear();
        HoverRelay = null;
        if (Card != null)
        {
            NativeCardPreviewRuntime.Show(
                Card,
                show: false,
                logComponent: "CollectionCardFactory"
            );
            Card.gameObject.SetActive(false);
        }
        if (Host != null)
        {
            Host.localScale = Vector3.one;
            Host.localRotation = Quaternion.identity;
            Host.anchoredPosition = Vector2.zero;
            Host.gameObject.SetActive(false);
        }
        if (CanvasGroup != null)
            CanvasGroup.alpha = 0f;
    }

    public void MarkMetricsDirty() => FrameMetrics = null;

    public void SetFrameMetrics(CollectionCardFrameMetrics metrics) => FrameMetrics = metrics;

    public CollectionCardHoverRelay? HoverRelay { get; set; }

    public void DestroyHost()
    {
        IsDestroyed = true;
        if (Host != null)
            Object.Destroy(Host.gameObject);
        Host = null;
        Socket = null;
        Card = null;
        Rect = null;
        Frame = null;
        CanvasGroup = null;
        HoverRelay = null;
    }

    private static RectTransform CreateHost(RectTransform parent, int layer, int instanceIndex)
    {
        var go = new GameObject(
            $"CollectionPanelCardHost_{Mathf.Max(0, instanceIndex)}",
            typeof(RectTransform)
        );
        go.layer = layer;
        var host = go.GetComponent<RectTransform>();
        host.SetParent(parent, worldPositionStays: false);
        host.anchorMin = new Vector2(0f, 1f);
        host.anchorMax = new Vector2(0f, 1f);
        host.pivot = new Vector2(0.5f, 0.5f);
        host.anchoredPosition = Vector2.zero;
        host.localScale = Vector3.one;
        return host;
    }

    private static TaskCompletionSource<object?> NewReadySource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static RectTransform? FindDescendant(Transform root, string childName)
    {
        foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
        {
            if (rt != null && rt.name == childName)
                return rt;
        }
        return null;
    }
}

internal readonly struct CollectionCardFrameMetrics
{
    public CollectionCardFrameMetrics(float width, float height, Vector2 centerOffset)
    {
        Width = width;
        Height = height;
        CenterOffset = centerOffset;
    }

    public float Width { get; }
    public float Height { get; }
    public Vector2 CenterOffset { get; }
}

internal sealed class CollectionCardFactoryStats
{
    public int ColdCreates { get; private set; }
    public int PoolReuses { get; private set; }
    public int PendingReturns { get; private set; }
    public int RebindFaults { get; private set; }

    public void RecordColdCreate() => ColdCreates++;
    public void RecordPoolReuse() => PoolReuses++;
    public void RecordPendingReturn() => PendingReturns++;
    public void RecordRebindFault() => RebindFaults++;
    public CollectionCardFactoryStatsSnapshot Snapshot() =>
        new(ColdCreates, PoolReuses, PendingReturns, RebindFaults);
}

internal readonly struct CollectionCardFactoryStatsSnapshot
{
    public CollectionCardFactoryStatsSnapshot(
        int coldCreates,
        int poolReuses,
        int pendingReturns,
        int rebindFaults
    )
    {
        ColdCreates = coldCreates;
        PoolReuses = poolReuses;
        PendingReturns = pendingReturns;
        RebindFaults = rebindFaults;
    }

    public int ColdCreates { get; }
    public int PoolReuses { get; }
    public int PendingReturns { get; }
    public int RebindFaults { get; }

    public CollectionCardFactoryStatsSnapshot DeltaFrom(CollectionCardFactoryStatsSnapshot start) =>
        new(
            ColdCreates - start.ColdCreates,
            PoolReuses - start.PoolReuses,
            PendingReturns - start.PendingReturns,
            RebindFaults - start.RebindFaults
        );
}
