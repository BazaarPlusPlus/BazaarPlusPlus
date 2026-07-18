#nullable enable
using HarmonyLib;
using TheBazaar;
using TheBazaar.UI;
using UnityEngine;

namespace BazaarPlusPlus.Game.Lobby.RandomHeroPool;

internal sealed class RandomHeroPoolNativeController : MonoBehaviour
{
    private static readonly System.Reflection.FieldInfo? HeroItemViewsField = AccessTools.Field(
        typeof(HeroSelectButtonsView),
        "HeroItemViews"
    );
    private static readonly System.Reflection.FieldInfo? HeroItemIsUnlockedField =
        AccessTools.Field(typeof(HeroItemView), "_isUnlocked");
    private static readonly System.Reflection.FieldInfo? IsProgrammaticSelectionField =
        AccessTools.Field(typeof(HeroSelectButtonsView), "_isProgrammaticSelection");
    private static readonly System.Reflection.MethodInfo? ShowSelectedMethod = AccessTools.Method(
        typeof(HeroItemView),
        "ShowSelected"
    );
    private static readonly List<WeakReference<RandomHeroPoolNativeController>> Controllers = new();
    private static bool _warnedMissingOwner;

    private HeroSelectButtonsView? _view;
    private HeroItemView[] _heroItems = Array.Empty<HeroItemView>();
    private readonly HashSet<HeroItemView> _unlockedItems = new();
    private NativePoolInteractionCoordinator<RandomHeroPoolState>? _coordinator;
    private bool _warnedMissingFields;
    private bool _registered;

    internal static void Attach(HeroSelectButtonsView view)
    {
        if (view == null)
            return;

        var controller = view.GetComponent<RandomHeroPoolNativeController>();
        if (controller == null)
            controller = view.gameObject.AddComponent<RandomHeroPoolNativeController>();

        controller._view = view;
        controller.Register();
        controller.RefreshStateAndVisuals();
    }

    internal static NativePoolInteractionRoute RouteSelection(HeroItemView item)
    {
        if (item == null)
            return NativePoolInteractionRoute.NativeAction;

        var controller = FindController(item);
        if (controller == null)
        {
            if (HeroSelectButtonsView.IsRandomHeroEnabled && !_warnedMissingOwner)
            {
                _warnedMissingOwner = true;
                LobbyLogWriter.ReportHeroPoolDegraded(
                    HeroPoolOperation.ResolveOwner,
                    LobbyLogReasonCode.OwnerUnavailable
                );
            }
            return NativePoolInteractionRoute.NativeAction;
        }
        if (controller._coordinator == null || controller._view == null)
            return NativePoolInteractionRoute.NativeAction;
        if (IsProgrammaticSelectionField == null)
        {
            controller.WarnMissingFieldsOnce();
            return NativePoolInteractionRoute.NativeAction;
        }

        var origin = NativePoolInteractionRouting.ResolveOrigin(
            HeroProgrammaticSelectionScope.IsActive(controller._view),
            IsProgrammaticSelectionField.GetValue(controller._view) is true
        );
        var route = controller._coordinator.HandleClick(
            HeroSelectButtonsView.IsRandomHeroEnabled,
            controller._unlockedItems.Contains(item),
            origin,
            item.Hero.ToString()
        );
        if (route == NativePoolInteractionRoute.PoolEdit)
            controller.ApplyVisuals();
        return route;
    }

    internal static bool TryProjectRandomModeItem(HeroItemView item)
    {
        if (item == null || !HeroSelectButtonsView.IsRandomHeroEnabled)
            return false;

        var controller = FindController(item);
        if (controller == null)
            return false;

        controller.ApplyItemVisual(item, poolModeEnabled: true);
        return true;
    }

    internal static void NotifyItemStarted(HeroItemView item)
    {
        var controller = FindController(item);
        if (controller == null)
            return;

        controller.ApplyItemVisual(item, HeroSelectButtonsView.IsRandomHeroEnabled);
    }

    private void OnDestroy()
    {
        _registered = false;
        CompactControllers();
    }

    private void Register()
    {
        if (_registered)
            return;

        Controllers.Add(new WeakReference<RandomHeroPoolNativeController>(this));
        _registered = true;
        CompactControllers();
    }

    private void RefreshStateAndVisuals()
    {
        if (_view == null || !TryReadHeroItems(_view, out var items))
            return;

        _heroItems = items;
        _unlockedItems.Clear();
        foreach (var item in items)
        {
            if (IsUnlocked(item))
                _unlockedItems.Add(item);
        }

        var unlockedIds = _unlockedItems.Select(item => item.Hero.ToString()).ToArray();
        if (!RandomHeroPoolPlayerPrefs.TryResolveState(unlockedIds, out var state) || state == null)
        {
            _coordinator = null;
            ApplyVisuals();
            return;
        }

        RandomHeroPoolPlayerPrefs.SaveSelectedHeroIds(state.SelectedHeroIds);
        _coordinator = new NativePoolInteractionCoordinator<RandomHeroPoolState>(
            state,
            (current, id) => current.IsSelected(id),
            (current, id, isSelected) => current.SetSelected(id, isSelected),
            current => RandomHeroPoolPlayerPrefs.SaveSelectedHeroIds(current.SelectedHeroIds)
        );
        ApplyVisuals();
    }

    private void ApplyVisuals()
    {
        var poolModeEnabled = HeroSelectButtonsView.IsRandomHeroEnabled;
        foreach (var item in _heroItems)
        {
            if (item != null)
                ApplyItemVisual(item, poolModeEnabled);
        }
    }

    private void ApplyItemVisual(HeroItemView item, bool poolModeEnabled)
    {
        var nativeSelected = Data.SelectedHero == item.Hero;
        var selected =
            _coordinator?.IsVisuallySelected(poolModeEnabled, item.Hero.ToString(), nativeSelected)
            ?? (!poolModeEnabled && nativeSelected);
        if (selected && (!poolModeEnabled || _unlockedItems.Contains(item)))
        {
            if (ShowSelectedMethod == null)
            {
                WarnMissingFieldsOnce();
                return;
            }

            ShowSelectedMethod.Invoke(item, null);
            return;
        }

        item.Deselect();
    }

    private bool TryReadHeroItems(HeroSelectButtonsView view, out HeroItemView[] heroItems)
    {
        if (HeroItemViewsField?.GetValue(view) is not IEnumerable<HeroItemView> reflectedItems)
        {
            heroItems = Array.Empty<HeroItemView>();
            WarnMissingFieldsOnce();
            return false;
        }

        heroItems = reflectedItems.Where(item => item != null).ToArray();
        return true;
    }

    private bool IsUnlocked(HeroItemView item)
    {
        if (HeroItemIsUnlockedField?.GetValue(item) is bool isUnlocked)
            return isUnlocked;

        WarnMissingFieldsOnce();
        return false;
    }

    private void WarnMissingFieldsOnce()
    {
        if (_warnedMissingFields)
            return;

        _warnedMissingFields = true;
        LobbyLogWriter.ReportHeroPoolDegraded(
            HeroPoolOperation.ResolveNativeFields,
            LobbyLogReasonCode.ReflectionUnavailable
        );
    }

    private bool Owns(HeroItemView item)
    {
        return _heroItems.Contains(item);
    }

    private static RandomHeroPoolNativeController? FindController(HeroItemView item)
    {
        for (var index = Controllers.Count - 1; index >= 0; index--)
        {
            if (
                !Controllers[index].TryGetTarget(out var controller)
                || controller == null
                || !controller._registered
            )
            {
                Controllers.RemoveAt(index);
                continue;
            }

            if (controller.Owns(item))
                return controller;
        }

        return null;
    }

    private static void CompactControllers()
    {
        for (var index = Controllers.Count - 1; index >= 0; index--)
        {
            if (
                !Controllers[index].TryGetTarget(out var controller)
                || controller == null
                || !controller._registered
            )
            {
                Controllers.RemoveAt(index);
            }
        }
    }
}

internal static class HeroProgrammaticSelectionScope
{
    private static readonly Dictionary<HeroSelectButtonsView, int> DepthByOwner = new();

    internal static int Enter(HeroSelectButtonsView owner)
    {
        DepthByOwner.TryGetValue(owner, out var previousDepth);
        DepthByOwner[owner] = previousDepth + 1;
        return previousDepth;
    }

    internal static void Restore(HeroSelectButtonsView owner, int previousDepth)
    {
        if (previousDepth <= 0)
            DepthByOwner.Remove(owner);
        else
            DepthByOwner[owner] = previousDepth;
    }

    internal static bool IsActive(HeroSelectButtonsView owner)
    {
        return DepthByOwner.TryGetValue(owner, out var depth) && depth > 0;
    }
}
