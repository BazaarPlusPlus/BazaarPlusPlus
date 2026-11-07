#nullable enable
using BazaarPlusPlus.GameInterop.HeroPortraits;
using Spine;
using Spine.Unity;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Game.HeroPortraits;

// Native parts share one animated skeleton. Only role transitions split meshes, preserving
// authored draw order without turning every attachment into a separate draw call.
internal sealed class NativePortraitLayeredRenderer : IDisposable
{
    private const int MaxParts = 24;
    private readonly SkeletonAnimation _skeleton;
    private readonly Transform _parent;
    private readonly PortraitBoneRoles _roles;
    private readonly Dictionary<(Attachment Attachment, int Bone), bool> _attachments = new();
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
        PortraitBoneRoles roles,
        Rect opening,
        Sprite extended,
        Rect extendedBounds
    )
    {
        _skeleton = skeleton;
        _parent = parent;
        _roles = roles;
        _extended = extended;
        _extendedBounds = extendedBounds;
        _strict = NativePortraitFrameMask.Create(opening, opening.yMax, out _strictBounds);
        _separator = skeleton.gameObject.AddComponent<SkeletonRenderSeparator>();
        _separator.copyMeshRendererFlags = false;
        _separator.SkeletonRenderer = skeleton;
    }

    internal static NativePortraitLayeredRenderer? TryCreate(
        SkeletonAnimation skeleton,
        Transform parent,
        Rect opening,
        Sprite extended,
        Rect extendedBounds
    )
    {
        var bones = skeleton.Skeleton.Data.Bones;
        var roles = new PortraitBoneRoles(
            bones.Select(b => b.Name).ToArray(),
            bones.Select(b => b.Parent?.Index ?? -1).ToArray()
        );
        if (!NeedsLayers(skeleton, roles, opening))
            return null;
        var view = new NativePortraitLayeredRenderer(
            skeleton,
            parent,
            roles,
            opening,
            extended,
            extendedBounds
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

    private static bool NeedsLayers(
        SkeletonAnimation animation,
        PortraitBoneRoles roles,
        Rect opening
    )
    {
        var foreground = false;
        var scenery = false;
        var transform = animation.transform;
        foreach (var slot in animation.Skeleton.DrawOrder)
        {
            if (!Drawable(slot))
                continue;
            if (Foreground(roles, slot))
            {
                foreground = true;
                continue;
            }
            float[] vertices;
            if (slot.Attachment is MeshAttachment mesh)
            {
                vertices = new float[mesh.WorldVerticesLength];
                mesh.ComputeWorldVertices(slot, 0, vertices.Length, vertices, 0, 2);
            }
            else if (slot.Attachment is RegionAttachment region)
            {
                vertices = new float[8];
                region.ComputeWorldVertices(slot, vertices, 0);
            }
            else
                continue;
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (var i = 0; i < vertices.Length; i += 2)
            {
                var point = new Vector2(
                    vertices[i] * transform.localScale.x + transform.localPosition.x,
                    vertices[i + 1] * transform.localScale.y + transform.localPosition.y
                );
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            var size = max - min;
            if (
                size.x >= opening.width * 0.4f
                && size.x * size.y >= opening.width * opening.height * 0.2f
                && max.y > opening.yMax - opening.height * 0.15f
                && min.x < opening.xMax
                && max.x > opening.xMin
            )
                scenery = true;
        }
        return foreground && scenery;
    }

    private static bool Drawable(Slot slot) =>
        slot.Bone.Active && slot.A > 0 && slot.Attachment is RegionAttachment or MeshAttachment;

    private static bool Foreground(PortraitBoneRoles roles, Slot slot) =>
        slot.Attachment is VertexAttachment mesh
            ? roles.IsForeground(slot.Bone.Data.Index, mesh.Bones, mesh.Vertices)
            : roles.IsForeground(slot.Bone.Data.Index, null, null);

    private bool IsForeground(Slot slot)
    {
        var key = (slot.Attachment, slot.Bone.Data.Index);
        if (!_attachments.TryGetValue(key, out var foreground))
        {
            foreground = Foreground(_roles, slot);
            _attachments.Add(key, foreground);
        }
        return foreground;
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
            var foreground = IsForeground(slot);
            if (previous.HasValue && previous.Value != foreground)
                _skeleton.separatorSlots.Add(slot);
            previous = foreground;
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
            var foreground = false;
            if (!_overflow)
            {
                var slots = item.skeleton.DrawOrder;
                for (var s = instructions.Items[start].startSlot; s < item.endSlot; s++)
                    if (Drawable(slots.Items[s]))
                    {
                        foreground = IsForeground(slots.Items[s]);
                        break;
                    }
            }
            var mask = _masks[part];
            var bounds = foreground ? _extendedBounds : _strictBounds;
            mask.sprite = foreground ? _extended : _strict;
            mask.transform.localPosition = bounds.center;
            mask.transform.localScale = new Vector3(bounds.width, bounds.height, 1);
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
        if (_strict != null)
            Object.Destroy(_strict);
    }
}
