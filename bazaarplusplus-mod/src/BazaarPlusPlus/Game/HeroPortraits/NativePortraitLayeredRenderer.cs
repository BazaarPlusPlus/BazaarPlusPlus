#nullable enable
using BazaarPlusPlus.GameInterop.HeroPortraits;
using Spine;
using Spine.Unity;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Game.HeroPortraits;

// Native parts share one animated skeleton. Only admission transitions split meshes, preserving
// authored draw order without turning every attachment into a separate draw call.
internal sealed class NativePortraitLayeredRenderer : IDisposable
{
    private const int MaxParts = PortraitBreakoutPlan.MaxParts;
    private readonly SkeletonAnimation _skeleton;
    private readonly Transform _parent;
    private readonly HashSet<string> _allowed;
    private readonly Dictionary<(Attachment Attachment, int Slot), bool> _attachments = new();
    private readonly Dictionary<Material, Material> _materials = new();
    private readonly HashSet<Material> _ownedMaterials = new();
    private readonly List<SpriteMask> _masks = new();
    private readonly Sprite _extended;
    private readonly Rect _extendedBounds;
    private readonly Sprite _strict;
    private readonly Rect _strictBounds;
    private readonly SkeletonRenderSeparator _separator;
    private bool _overflow;
    private bool _disposed;
    private bool _suppressRendering = true;

    internal int PartCount => _separator.partsRenderers.Count;

    internal void SuppressRendering(bool suppress)
    {
        if (_suppressRendering == suppress)
            return;
        _suppressRendering = suppress;
        foreach (var part in _separator.partsRenderers)
            if (part != null)
                part.MeshRenderer.forceRenderingOff = suppress;
    }

    private NativePortraitLayeredRenderer(
        SkeletonAnimation skeleton,
        Transform parent,
        Sprite strict,
        Rect strictBounds,
        Sprite extended,
        Rect extendedBounds,
        string[] allowed
    )
    {
        _skeleton = skeleton;
        _parent = parent;
        _allowed = new HashSet<string>(allowed, StringComparer.Ordinal);
        _extended = extended;
        _extendedBounds = extendedBounds;
        _strict = strict;
        _strictBounds = strictBounds;
        _separator = skeleton.gameObject.AddComponent<SkeletonRenderSeparator>();
        _separator.copyMeshRendererFlags = false;
        _separator.SkeletonRenderer = skeleton;
    }

    internal static NativePortraitLayeredRenderer? TryCreate(
        SkeletonAnimation skeleton,
        Transform parent,
        Sprite strict,
        Rect strictBounds,
        Sprite extended,
        Rect extendedBounds,
        string[] allowed
    )
    {
        if (allowed.Length == 0)
            return null;
        var view = new NativePortraitLayeredRenderer(
            skeleton,
            parent,
            strict,
            strictBounds,
            extended,
            extendedBounds,
            allowed
        );
        try
        {
            skeleton.singleSubmesh = false;
            skeleton.disableRenderingOnOverride = true;
            skeleton.updateWhenInvisible = UpdateMode.FullUpdate;
            skeleton.maskInteraction = SpriteMaskInteraction.None;
            skeleton.UpdateComplete += view.UpdateRoles;
            // Subscribe before the native separator so its instructions receive stable
            // masked material clones before RenderParts builds or refreshes materials.
            skeleton.GenerateMeshOverride += view.PrepareParts;
            view.UpdateRoles(skeleton);
            view._separator.OnEnable();
            skeleton.LateUpdateMesh();
            return view;
        }
        catch
        {
            view.Dispose();
            throw;
        }
    }

    private static bool Drawable(Slot slot) =>
        slot.Bone.Active
        && slot.A > 0
        && (slot.Attachment is RegionAttachment { A: > 0 } or MeshAttachment { A: > 0 });

    private bool AllowsBreakout(Slot slot)
    {
        var key = (slot.Attachment, slot.Data.Index);
        if (!_attachments.TryGetValue(key, out var allowed))
        {
            allowed = _allowed.Contains(
                NativePortraitPoseCapture.PartKey(slot.Data.Index, slot.Attachment.Name)
            );
            _attachments.Add(key, allowed);
        }
        return allowed;
    }

    private void UpdateRoles(ISkeletonAnimation animation)
    {
        if (_disposed)
            return;
        _skeleton.separatorSlots.Clear();
        bool? previous = null;
        _overflow = false;
        foreach (var slot in _skeleton.Skeleton.DrawOrder)
        {
            if (!Drawable(slot))
                continue;
            var allowed = AllowsBreakout(slot);
            if (previous.HasValue && previous.Value != allowed)
                _skeleton.separatorSlots.Add(slot);
            previous = allowed;
            if (_skeleton.separatorSlots.Count >= MaxParts)
            {
                // Keep the complete pose, strictly clipped, rather than dropping the tail
                // of the native draw list or allocating unbounded parts and masks.
                _skeleton.separatorSlots.Clear();
                _overflow = true;
                break;
            }
        }
    }

    private void PrepareParts(SkeletonRendererInstruction instruction)
    {
        if (_disposed)
            return;
        var instructions = instruction.submeshInstructions;
        var part = 0;
        var start = 0;
        for (var i = 0; i < instructions.Count; i++)
        {
            var item = instructions.Items[i];
            if (item.material != null)
            {
                item.material = Masked(item.material);
                instructions.Items[i] = item;
            }
            if (!item.forceSeparate && i != instructions.Count - 1)
                continue;
            EnsurePart(part);
            var allowed = false;
            if (!_overflow)
            {
                var slots = item.skeleton.DrawOrder;
                for (var s = instructions.Items[start].startSlot; s < item.endSlot; s++)
                    if (Drawable(slots.Items[s]))
                    {
                        allowed = AllowsBreakout(slots.Items[s]);
                        break;
                    }
            }
            var mask = _masks[part];
            var bounds = allowed ? _extendedBounds : _strictBounds;
            var sprite = allowed ? _extended : _strict;
            if (mask.sprite != sprite)
            {
                mask.sprite = sprite;
                mask.transform.localPosition = bounds.center;
                mask.transform.localScale = new Vector3(
                    bounds.width / sprite.bounds.size.x,
                    bounds.height / sprite.bounds.size.y,
                    1
                );
            }
            if (!mask.gameObject.activeSelf)
                mask.gameObject.SetActive(true);
            part++;
            start = i + 1;
        }
        for (var i = part; i < _masks.Count; i++)
            _masks[i].gameObject.SetActive(false);
    }

    private void EnsurePart(int index)
    {
        while (_separator.partsRenderers.Count <= index)
        {
            var number = _separator.partsRenderers.Count;
            var part = _separator.AddPartsRenderer(3, "BPP_PortraitPart");
            part.gameObject.layer = _skeleton.gameObject.layer;
            part.MeshRenderer.sortingLayerID = 0;
            part.MeshRenderer.sortingOrder = number * 3;
            part.MeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            part.MeshRenderer.receiveShadows = false;
            part.MeshRenderer.forceRenderingOff = _suppressRendering;
            var maskObject = new GameObject("BPP_PortraitPartMask");
            maskObject.layer = _parent.gameObject.layer;
            maskObject.transform.SetParent(_parent, false);
            var mask = maskObject.AddComponent<SpriteMask>();
            mask.alphaCutoff = 0.5f;
            mask.isCustomRangeActive = true;
            mask.frontSortingLayerID = mask.backSortingLayerID = 0;
            mask.frontSortingOrder = number * 3 + 1;
            mask.backSortingOrder = number * 3 - 1;
            _masks.Add(mask);
        }
    }

    private Material Masked(Material source)
    {
        if (_ownedMaterials.Contains(source))
            return source;
        if (_materials.TryGetValue(source, out var material))
            return material;
        if (!source.HasProperty("_StencilComp"))
            throw new InvalidOperationException(
                "The skin material does not support portrait masks."
            );
        material = new Material(source);
        material.SetFloat("_StencilComp", (float)CompareFunction.LessEqual);
        _materials.Add(source, material);
        _ownedMaterials.Add(material);
        return material;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_skeleton != null)
        {
            _skeleton.UpdateComplete -= UpdateRoles;
            _skeleton.GenerateMeshOverride -= PrepareParts;
        }
        if (_separator != null)
            _separator.SkeletonRenderer = null;
        foreach (var material in _ownedMaterials)
            if (material != null)
                Object.Destroy(material);
        _ownedMaterials.Clear();
        _materials.Clear();
    }
}
