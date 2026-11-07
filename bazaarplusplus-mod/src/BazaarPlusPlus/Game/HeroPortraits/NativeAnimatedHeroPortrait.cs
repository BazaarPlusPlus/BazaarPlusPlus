#nullable enable
using BazaarPlusPlus.GameInterop.HeroPortraits;
using BazaarPlusPlus.Infrastructure.HeroPortraits.Composition;
using SkinEdit;
using Spine.Unity;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Game.HeroPortraits;

// Own only the cloned character, its mask, and the materials Spine creates for that mask.
// The original sprite remains assigned for native swaps, bounds, inspection, and interaction.
internal sealed class NativeAnimatedHeroPortrait : IDisposable
{
    private readonly SpriteRenderer _portrait;
    private readonly Sprite _sprite;
    private readonly bool _originalForceRenderingOff;
    private readonly GameObject _root;
    private readonly SortingGroup _sortingGroup;
    private readonly HashSet<Material> _ownedMaskMaterials = new();
    private SkeletonAnimation? _skeleton;
    private MeshRenderer? _renderer;
    private SpriteMask? _mask;
    private Sprite? _maskSprite;
    private Sprite? _breakoutSprite;
    private Texture2D? _breakoutTexture;
    private NativePortraitLayeredRenderer? _layers;
    private bool _disposed;
    private bool _published;
    private bool _renderSuppressed = true;
    private readonly PortraitPublicationState _publication = new();
    private bool _directAppearance;
    private bool _ownsInitialSuppression;

    internal PortraitComposition Composition { get; }
    internal bool Layered => _layers != null;
    internal int RenderParts => _layers?.PartCount ?? 1;

    private NativeAnimatedHeroPortrait(
        SpriteRenderer portrait,
        PortraitComposition composition,
        bool? originalForceOff,
        bool directAppearance
    )
    {
        Composition = composition;
        _portrait = portrait;
        _sprite = portrait.sprite;
        _originalForceRenderingOff = originalForceOff ?? portrait.forceRenderingOff;
        _directAppearance = directAppearance;
        _root = new GameObject("BPP_AnimatedHeroPortrait");
        _root.transform.SetParent(portrait.transform, false);
        _root.SetActive(false);
        _root.layer = portrait.gameObject.layer;
        _sortingGroup = _root.AddComponent<SortingGroup>();
    }

    internal static async Task<NativeAnimatedHeroPortrait?> CreateAsync(
        SpriteRenderer portrait,
        NativeHeroPortraitSkin skin,
        GameObject prefab,
        NativePortraitComposition compositions,
        PortraitBreakoutPreparation breakouts,
        CancellationToken cancellation,
        bool? originalForceOff = null,
        Func<bool>? keepStaticHidden = null,
        Action? cacheMiss = null
    )
    {
        var sprite = portrait.sprite;
        var source = FindSource(prefab);
        var animationName = skin.IdleAnimation;
        PortraitCapturedImages? images = null;
        var composition = await compositions.MatchAsync(
            sprite,
            source,
            skin,
            animationName,
            cancellation,
            cacheMiss,
            captured => images = captured
        );
        cancellation.ThrowIfCancellationRequested();
        if (portrait == null || portrait.sprite != sprite || prefab == null)
            throw new OperationCanceledException();
        if (composition == null)
            return null;
        var opening = NativePortraitFrameMask.ReadOpening(portrait);
        var frame = NativePortraitFrameMask.Normalize(opening, sprite);
        var plan = await breakouts.PrepareAsync(
            sprite,
            source,
            animationName,
            composition,
            frame,
            images,
            cacheMiss,
            cancellation
        );
        cancellation.ThrowIfCancellationRequested();
        if (portrait == null || portrait.sprite != sprite || prefab == null)
            throw new OperationCanceledException();
        var view = new NativeAnimatedHeroPortrait(
            portrait,
            composition,
            originalForceOff,
            keepStaticHidden?.Invoke() == true
        );
        try
        {
            view.Build(skin, source, composition, opening, plan);
            return view;
        }
        catch
        {
            view.Dispose();
            throw;
        }
    }

    private static SkeletonAnimation FindSource(GameObject prefab)
    {
        var placement = prefab.transform.Find(ESkinPlacement.CollectionsScreen.ToString());
        if (placement == null && prefab.GetComponent<SkinEditComponent>() != null)
            throw new InvalidOperationException("The skin has no CollectionsScreen placement.");
        var source = (placement ?? prefab.transform).GetComponentInChildren<SkeletonAnimation>(
            true
        );
        if (source == null)
            throw new InvalidOperationException("The skin has no character SkeletonAnimation.");
        return source;
    }

    private void Build(
        NativeHeroPortraitSkin skin,
        SkeletonAnimation source,
        PortraitComposition composition,
        Rect opening,
        PortraitBreakoutPlan plan
    )
    {
        var character = Object.Instantiate(source.gameObject, _root.transform, false);
        character.layer = _portrait.gameObject.layer;
        character.SetActive(true);
        _skeleton = character.GetComponent<SkeletonAnimation>();
        // Reset editor-specific effects and splits. Optional portrait role groups are
        // generated later with their own frame masks.
        foreach (var behaviour in character.GetComponents<Behaviour>())
            if (behaviour != _skeleton)
                behaviour.enabled = false;
        foreach (Transform child in character.transform)
            child.gameObject.SetActive(false);
        _skeleton.Initialize(false);
        _skeleton.enabled = true;
        _skeleton.separatorSlots.Clear();
        _skeleton.maskInteraction = SpriteMaskInteraction.None;
        _skeleton.maskMaterials = new SkeletonRenderer.SpriteMaskInteractionMaterials();
        _skeleton.OnMeshAndMaterialsUpdated += TrackMaskMaterials;
        var idle =
            _skeleton.Skeleton.Data.FindAnimation(skin.IdleAnimation)
            ?? _skeleton.Skeleton.Data.FindAnimation("idle");
        if (idle == null && !string.IsNullOrEmpty(source.AnimationName))
            idle = _skeleton.Skeleton.Data.FindAnimation(source.AnimationName);
        if (idle == null)
            throw new InvalidOperationException("The skin has no display idle animation.");
        var track = _skeleton.AnimationState.SetAnimation(0, idle, true);
        track.TrackTime = composition.PoseTime;
        _skeleton.Update(0f);
        _skeleton.LateUpdate();
        _renderer = character.GetComponent<MeshRenderer>();
        _renderer.enabled = true;
        _renderer.forceRenderingOff = true;
        var mesh = character.GetComponent<MeshFilter>().sharedMesh;
        if (mesh == null)
            throw new InvalidOperationException("The skin has no character mesh.");
        var target = _sprite.bounds;
        if (mesh.bounds.size.y <= 0f || target.size.y <= 0f)
            throw new InvalidOperationException("The skin has no usable character bounds.");
        // The fit is in reference-image-height units. Convert once into the Sprite's own
        // coordinates; its parent already carries the game's authored skin offsets/scale.
        var height = _sprite.rect.height / _sprite.pixelsPerUnit;
        var applied = plan.Apply(composition);
        var scale = applied.Scale * height;
        character.transform.localRotation = Quaternion.identity;
        character.transform.localScale = Vector3.one * scale;
        character.transform.localPosition = new Vector3(
            applied.X * height - _sprite.pivot.x / _sprite.pixelsPerUnit,
            applied.Y * height - _sprite.pivot.y / _sprite.pixelsPerUnit,
            0f
        );
        _skeleton.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;

        var maskObject = new GameObject("BPP_HeroPortraitMask");
        maskObject.layer = _portrait.gameObject.layer;
        maskObject.transform.SetParent(_root.transform, false);
        _maskSprite = NativePortraitFrameMask.Create(opening, out var maskBounds);
        maskObject.transform.localPosition = maskBounds.center;
        maskObject.transform.localScale = new Vector3(maskBounds.width, maskBounds.height, 1f);
        _mask = maskObject.AddComponent<SpriteMask>();
        _mask.alphaCutoff = 0.5f;
        _mask.sprite = _maskSprite;
        _mask.isCustomRangeActive = true;
        if (plan.Parts.Length != 0)
        {
            var interior = plan.Interior();
            var pixels = new byte[plan.Width * plan.Height * 4];
            for (var i = 0; i < interior.Length; i++)
                if (interior[i])
                    pixels[i * 4 + 3] = 255;
            for (var i = 0; i < plan.Runs.Length; i += 2)
            for (var j = plan.Runs[i]; j < plan.Runs[i] + plan.Runs[i + 1]; j++)
                pixels[j * 4 + 3] = 255;
            _breakoutSprite = NativePortraitFrameMask.Create(
                new PortraitImage(plan.Width, plan.Height, pixels),
                out _breakoutTexture
            );
            var extendedBounds = Rect.MinMaxRect(
                plan.Bounds.Left * height - _sprite.pivot.x / _sprite.pixelsPerUnit,
                plan.Bounds.Bottom * height - _sprite.pivot.y / _sprite.pixelsPerUnit,
                plan.Bounds.Right * height - _sprite.pivot.x / _sprite.pixelsPerUnit,
                plan.Bounds.Top * height - _sprite.pivot.y / _sprite.pixelsPerUnit
            );
            _layers = NativePortraitLayeredRenderer.TryCreate(
                _skeleton,
                _root.transform,
                _maskSprite,
                maskBounds,
                _breakoutSprite,
                extendedBounds,
                plan.Parts
            );
        }
        if (_layers != null)
            _mask.gameObject.SetActive(false);
        // Build the masked materials/parts while hidden, not on the first visible frame.
        SuppressRendering(true);
        _skeleton.LateUpdateMesh();
        // The slot publishes only after its final cancellation/sprite identity check.
    }

    internal void Sync()
    {
        if (_disposed || _portrait == null || _portrait.sprite != _sprite)
        {
            SuppressRendering(true);
            _publication.Hide();
            RestoreSprite();
            if (_root != null)
                _root.SetActive(false);
            return;
        }
        var visible =
            _portrait.enabled
            && _portrait.gameObject.activeInHierarchy
            && !_originalForceRenderingOff
            && _portrait.color.a > 0f;
        if (_renderer == null || _mask == null || _skeleton?.Skeleton == null)
            return;
        if (!visible)
        {
            SuppressRendering(true);
            _publication.Hide();
            if (_directAppearance)
                HoldStatic();
            else
                RestoreSprite();
            _root.SetActive(false);
            return;
        }
        // A local sorting group prevents one hero's mask from revealing the other's mesh.
        _sortingGroup.sortingLayerID = _portrait.sortingLayerID;
        _sortingGroup.sortingOrder = _portrait.sortingOrder;
        if (_layers == null)
        {
            _renderer.sortingLayerID = 0;
            _renderer.sortingOrder = 0;
            _mask.frontSortingLayerID = _mask.backSortingLayerID = 0;
            _mask.frontSortingOrder = 1;
            _mask.backSortingOrder = -1;
        }
        var color = _portrait.color;
        _skeleton.Skeleton.R = color.r;
        _skeleton.Skeleton.G = color.g;
        _skeleton.Skeleton.B = color.b;
        _skeleton.Skeleton.A = color.a;
        if (!_root.activeSelf)
        {
            SuppressRendering(true);
            if (_directAppearance)
                HoldStatic();
            else
                RestoreSprite();
            _publication.Stage(Time.frameCount);
            _root.SetActive(true);
            // OnEnable can rebuild native renderers. Finish their masked mesh now,
            // even when this controller runs after SkeletonAnimation.LateUpdate.
            _skeleton.LateUpdateMesh();
            return;
        }
        if (!_publication.CanPublish(Time.frameCount))
            return;
        SuppressRendering(false);
        _portrait.forceRenderingOff = true;
        _published = true;
        _directAppearance = true;
    }

    internal void AdoptStaticSuppression() => _ownsInitialSuppression = true;

    private void HoldStatic()
    {
        if (!_published && !_ownsInitialSuppression)
            _portrait.forceRenderingOff = true;
        _ownsInitialSuppression = true;
    }

    private void SuppressRendering(bool suppress)
    {
        if (_renderSuppressed == suppress)
            return;
        _renderSuppressed = suppress;
        if (_renderer != null)
            _renderer.forceRenderingOff = suppress || _layers != null;
        _layers?.SuppressRendering(suppress);
    }

    private void RestoreSprite()
    {
        if ((_published || _ownsInitialSuppression) && _portrait != null)
            _portrait.forceRenderingOff = _originalForceRenderingOff;
        _published = false;
        _ownsInitialSuppression = false;
    }

    private void TrackMaskMaterials(SkeletonRenderer renderer)
    {
        foreach (var material in renderer.maskMaterials.materialsInsideMask)
            if (material != null)
                _ownedMaskMaterials.Add(material);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _layers?.Dispose();
        RestoreSprite();
        if (_skeleton != null)
            _skeleton.OnMeshAndMaterialsUpdated -= TrackMaskMaterials;
        foreach (var material in _ownedMaskMaterials)
            if (material != null)
                Object.Destroy(material);
        _ownedMaskMaterials.Clear();
        if (_root != null)
        {
            _root.SetActive(false);
            Object.Destroy(_root);
        }
        if (_maskSprite != null)
            Object.Destroy(_maskSprite);
        if (_breakoutSprite != null)
            Object.Destroy(_breakoutSprite);
        if (_breakoutTexture != null)
            Object.Destroy(_breakoutTexture);
    }
}
