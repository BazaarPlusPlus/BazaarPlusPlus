#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.GameInterop.AssetLoading;
using BazaarPlusPlus.GameInterop.HeroPortraits;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using TheBazaar.AppFramework;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using UnityEngine;

namespace BazaarPlusPlus.Game.HeroPortraits;

internal sealed class AnimatedHeroPortraitController : MonoBehaviour
{
    private readonly Dictionary<SpriteRenderer, Slot> _slots = new();
    private readonly List<SpriteRenderer> _portraits = new();
    private readonly List<SpriteRenderer> _removed = new();
    private BppSettings? _config;
    private NativeHeroPortraitCatalog? _catalog;
    private float _nextRefresh;
    private NativePortraitComposition? _compositions;
    private PortraitBreakoutPreparation? _breakouts;
    private long _setupGeneration;

    internal void Initialize(
        BppSettings config,
        NativeHeroPortraitCatalog catalog,
        string cacheDirectory
    )
    {
        _config = config;
        _catalog = catalog;
        _compositions = new NativePortraitComposition(cacheDirectory);
        _breakouts = new PortraitBreakoutPreparation(cacheDirectory);
    }

    private void Update()
    {
        if (_config == null || !_config.EnableAnimatedHeroPortraitsConfig.Value)
        {
            Clear();
            return;
        }
        // Release stale visuals at frame cadence; discovery alone is throttled.
        _removed.Clear();
        foreach (var pair in _slots)
        {
            pair.Value.CheckPreparationTimeout();
            if (
                pair.Key == null
                || pair.Value.SetupSuperseded
                || (!pair.Value.AwaitingNative && pair.Key.sprite != pair.Value.Sprite)
            )
            {
                pair.Value.Dispose();
                _removed.Add(pair.Key!);
            }
        }
        foreach (var portrait in _removed)
            _slots.Remove(portrait);
        if (_catalog == null || Time.unscaledTime < _nextRefresh)
            return;
        _nextRefresh = Time.unscaledTime + 0.25f;
        _catalog.CopyPortraits(_portraits);
        foreach (var portrait in _portraits)
        {
            if (
                portrait.sprite == null
                || !portrait.gameObject.activeInHierarchy
                || !portrait.enabled
                || _slots.ContainsKey(portrait)
            )
                continue;
            if (!_catalog.TryResolve(portrait.sprite, out var skin, out _))
                continue;
            if (!Services.TryGet<AssetLoader>(out var loader) || loader == null)
                continue;
            if (_compositions == null)
                continue;
            var slot = new Slot(this, portrait, portrait.sprite, _compositions);
            _slots.Add(portrait, slot);
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Plugin, "plugin.hero_portrait.load_started"),
                ("skin", skin.Name),
                ("sprite", portrait.sprite.name)
            );
            _ = slot.LoadAsync(loader, skin);
        }
    }

    private void LateUpdate()
    {
        foreach (var slot in _slots.Values)
            slot.Sync();
    }

    private void Clear()
    {
        foreach (var slot in _slots.Values)
            slot.Dispose();
        _slots.Clear();
        _portraits.Clear();
    }

    private void OnDestroy() => Clear();

    internal long BeforeSetup(EncounterController encounter, EncounterAssetDataSO data)
    {
        var generation = ++_setupGeneration;
        if (
            encounter == null
            || encounter.Portrait == null
            || !encounter.Portrait.TryGetComponent<SpriteRenderer>(out var portrait)
        )
            return generation;
        if (_slots.TryGetValue(portrait, out var previous))
        {
            previous.Dispose();
            _slots.Remove(portrait);
        }
        if (
            _config == null
            || !_config.EnableAnimatedHeroPortraitsConfig.Value
            || _catalog == null
            || _compositions == null
        )
            return generation;
        var sprite = data.portraitTextureReference;
        if (
            sprite == null
            || !_catalog.TryResolve(sprite, out var skin, out _)
            || !_compositions.HasPreparedCandidate(sprite, skin)
            || !Services.TryGet<AssetLoader>(out var loader)
            || loader == null
        )
            return generation;
        var state = new PortraitCachedSetupState(generation);
        var slot = new Slot(this, portrait, sprite, _compositions, state, encounter, data);
        _slots.Add(portrait, slot);
        _ = slot.LoadAsync(loader, skin);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Plugin, "plugin.hero_portrait.cached_setup"),
            ("skin", skin.Name),
            ("sprite", sprite.name)
        );
        return generation;
    }

    internal void PortraitApplied(SpriteRenderer portrait)
    {
        if (portrait == null)
            return;
        if (_slots.TryGetValue(portrait, out var slot))
            slot.CommitNative();
    }

    internal void SetupFinished(EncounterController encounter, long generation)
    {
        if (
            encounter == null
            || encounter.Portrait == null
            || !encounter.Portrait.TryGetComponent<SpriteRenderer>(out var portrait)
        )
            return;
        if (
            _slots.TryGetValue(portrait, out var slot)
            && slot.Generation == generation
            && slot.AwaitingNative
        )
        {
            slot.Dispose();
            _slots.Remove(portrait);
        }
    }

    private bool IsCurrent(Slot slot, SpriteRenderer portrait) =>
        _slots.TryGetValue(portrait, out var current) && ReferenceEquals(slot, current);

    private sealed class Slot : IDisposable
    {
        private readonly SpriteRenderer _portrait;
        private readonly AnimatedHeroPortraitController _owner;
        private readonly PortraitCachedSetupState? _cachedSetup;
        private readonly EncounterController? _encounter;
        private readonly EncounterAssetDataSO? _expectedData;
        private readonly TaskCompletionSource<bool>? _nativeReady;
        private readonly bool _originalForceOff;
        private readonly float _preparationStarted;
        private NativeAnimatedHeroPortrait? _view;
        private bool _disposed;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly NativePortraitComposition _compositions;
        private bool _loadCompleted;
        internal Sprite Sprite { get; }
        internal long Generation => _cachedSetup?.Generation ?? 0;
        internal bool AwaitingNative => _cachedSetup?.AwaitingNative ?? false;
        internal bool SetupSuperseded =>
            _cachedSetup != null
            && (_encounter == null || _encounter.CurrentEncounterDataObject != _expectedData);

        internal Slot(
            AnimatedHeroPortraitController owner,
            SpriteRenderer portrait,
            Sprite sprite,
            NativePortraitComposition compositions,
            PortraitCachedSetupState? cachedSetup = null,
            EncounterController? encounter = null,
            EncounterAssetDataSO? expectedData = null
        )
        {
            _owner = owner;
            _portrait = portrait;
            Sprite = sprite;
            _compositions = compositions;
            _cachedSetup = cachedSetup;
            _encounter = encounter;
            _expectedData = expectedData;
            _originalForceOff = portrait.forceRenderingOff;
            _preparationStarted = Time.unscaledTime;
            if (cachedSetup != null)
            {
                _nativeReady = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                portrait.forceRenderingOff = true;
            }
        }

        internal void CommitNative()
        {
            if (
                !_disposed
                && _cachedSetup != null
                && !SetupSuperseded
                && _portrait.sprite == Sprite
                && _cachedSetup.Commit(Generation)
            )
                _nativeReady!.TrySetResult(true);
        }

        internal void CheckPreparationTimeout()
        {
            if (
                _cachedSetup?.HoldsStatic == true
                && Time.unscaledTime - _preparationStarted >= 1.5f
            )
                ReleaseStatic();
        }

        private void ReleaseStatic()
        {
            if (_cachedSetup?.ReleaseStatic() == true && _portrait != null)
                _portrait.forceRenderingOff = _originalForceOff;
        }

        internal async Task LoadAsync(AssetLoader loader, NativeHeroPortraitSkin skin)
        {
            var skinName = skin.Name;
            try
            {
                var prefab = await NativeGlobalAssetLoader.LoadByReferenceAsync<GameObject>(
                    loader,
                    skin.CharacterPrefab!,
                    NativeAssetScopeIntent.Current
                );
                if (_nativeReady != null)
                    await _nativeReady.Task;
                if (
                    _disposed
                    || _portrait == null
                    || _portrait.sprite != Sprite
                    || prefab == null
                    || SetupSuperseded
                    || !_owner.IsCurrent(this, _portrait)
                )
                    return;
                var view = await NativeAnimatedHeroPortrait.CreateAsync(
                    _portrait,
                    skin,
                    prefab,
                    _compositions,
                    _owner._breakouts!,
                    _cancellation.Token,
                    _cachedSetup == null ? null : _originalForceOff,
                    () => _cachedSetup?.HoldsStatic == true,
                    ReleaseStatic
                );
                if (
                    _disposed
                    || _portrait == null
                    || _portrait.sprite != Sprite
                    || SetupSuperseded
                    || !_owner.IsCurrent(this, _portrait)
                )
                {
                    view?.Dispose();
                    return;
                }
                if (view != null && _cachedSetup?.ReleaseStatic() == true)
                    view.AdoptStaticSuppression();
                _view = view;
                BppLog.InfoEvent(
                    new BppLogEvent(BppLogFeatureScope.Plugin, "plugin.hero_portrait.composition"),
                    new BppLogField[]
                    {
                        ("skin", skinName),
                        ("sprite", Sprite.name),
                        ("matched", view != null),
                        ("inliers", view?.Composition.Inliers ?? 0),
                        ("fit_error", view?.Composition.Error ?? 0f),
                        ("layered", view?.Layered ?? false),
                        ("render_parts", view?.RenderParts ?? 0),
                    }
                );
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // Keep a failed slot until the sprite changes or the user toggles the setting;
                // a broken skin must not retry or emit a warning every discovery tick.
                if (!_disposed)
                {
                    BppLog.WarnEvent(
                        new BppLogEvent(
                            BppLogFeatureScope.Plugin,
                            "plugin.hero_portrait.unavailable",
                            storm: ["skin"]
                        ),
                        ex,
                        ("skin", skinName)
                    );
                }
            }
            finally
            {
                if (_view == null)
                    ReleaseStatic();
                _loadCompleted = true;
                if (_disposed)
                    _cancellation.Dispose();
            }
        }

        internal void Sync() => _view?.Sync();

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _cancellation.Cancel();
            _nativeReady?.TrySetCanceled();
            if (_loadCompleted)
                _cancellation.Dispose();
            _view?.Dispose();
            ReleaseStatic();
            _view = null;
        }
    }
}
