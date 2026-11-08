#nullable enable
using BazaarPlusPlus.GameInterop.HeroPortraits;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.HeroPortraits.Composition;
using BazaarPlusPlus.Infrastructure.Logging;
using Spine;
using Spine.Unity;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Game.HeroPortraits;

// The native renderer owns the strict interior. This companion transports cached UV
// cuts with Spine's live vertices; a disjoint GPU mask admits only the bounded exterior.
internal sealed class NativePortraitLayeredRenderer : IDisposable
{
    private const int MaxVertices = 100_000;

    private sealed class Generator : MeshGenerator
    {
        internal ExposedList<ExposedList<int>> Indices => submeshes;
    }

    private readonly record struct Key(Layout Layout, Vector2 A, Vector2 B, Vector2 C);

    private readonly record struct Weighted(Vector3 Weights, Vector2 Uv);

    private sealed record Template(Key Key, Weighted[] Vertices);

    private readonly SkeletonAnimation _skeleton;
    private readonly Generator _generator = new();
    private readonly SkeletonRendererInstruction _instruction = new();
    private readonly List<Slot> _separators = new();
    private readonly List<(SubmeshInstruction Instruction, Layout Layout)> _items = new();
    private readonly Dictionary<Material, Material> _materialsBySource = new();
    private readonly Dictionary<Key, LinkedListNode<Template>> _cache = new();
    private readonly LinkedList<Template> _recent = new();
    private readonly Mesh _source = new() { name = "BPP_PortraitNativeGeometry" };
    private readonly Mesh _mesh = new()
    {
        name = "BPP_PortraitRegionGeometry",
        indexFormat = IndexFormat.UInt32,
    };
    private readonly MeshRenderer _renderer;
    private readonly GameObject _object,
        _ringObject;
    private readonly Sprite _ring;
    private readonly Dictionary<string, Layout> _regions;
    private readonly Dictionary<AtlasPage, int> _pages;
    private readonly Dictionary<(Attachment, int), Layout?> _attachments = new();
    private readonly Plane _bottom;
    private readonly PortraitFrameGeometry _opening;
    private readonly PortraitComposition _fit;
    private readonly List<Vector3> _positions = new(),
        _normals = new(),
        _sourceNormals = new();
    private readonly List<Vector2> _uvs = new(),
        _uv2 = new(),
        _uv3 = new();
    private readonly List<Vector4> _tangents = new(),
        _sourceTangents = new();
    private readonly List<Color32> _colors = new();
    private readonly List<Material> _materials = new();
    private readonly List<List<int>> _triangles = new();
    private readonly List<Weighted> _weights = new();
    private readonly MaterialPropertyBlock _properties = new();
    private readonly Vertex[] _a = new Vertex[64],
        _b = new Vertex[64],
        _outside = new Vertex[64];
    private readonly Vertex[] _boxA = new Vertex[64],
        _boxB = new Vertex[64];
    private int _submesh,
        _work,
        _cachedVertices;
    private bool _disposed,
        _suppressed = true;

    internal bool Failed { get; private set; }
    internal int PartCount => _positions.Count == 0 ? 1 : 2;

    private readonly struct Plane(float a, float b, float c, bool uv = false)
    {
        internal float Distance(Vertex p) =>
            uv ? a * p.Uv.x + b * p.Uv.y + c : a * p.Position.x + b * p.Position.y + c;
    }

    private struct Vertex
    {
        internal Vector3 Position,
            Normal;
        internal Vector2 Uv,
            Uv2,
            Uv3;
        internal Vector4 Tangent;
        internal Color Color;

        internal static Vertex Lerp(Vertex a, Vertex b, float t) =>
            new()
            {
                Position = Vector3.LerpUnclamped(a.Position, b.Position, t),
                Normal = Vector3.LerpUnclamped(a.Normal, b.Normal, t),
                Uv = Vector2.LerpUnclamped(a.Uv, b.Uv, t),
                Uv2 = Vector2.LerpUnclamped(a.Uv2, b.Uv2, t),
                Uv3 = Vector2.LerpUnclamped(a.Uv3, b.Uv3, t),
                Tangent = Vector4.LerpUnclamped(a.Tangent, b.Tangent, t),
                Color = Color.LerpUnclamped(a.Color, b.Color, t),
            };

        internal static Vertex Apply(
            Weighted p,
            Vertex a,
            Vertex b,
            Vertex c,
            bool tint,
            bool normals,
            bool tangents
        )
        {
            var w = p.Weights;
            return new Vertex
            {
                Position = a.Position * w.x + b.Position * w.y + c.Position * w.z,
                Uv = p.Uv,
                Color = a.Color * w.x + b.Color * w.y + c.Color * w.z,
                Uv2 = tint ? a.Uv2 * w.x + b.Uv2 * w.y + c.Uv2 * w.z : default,
                Uv3 = tint ? a.Uv3 * w.x + b.Uv3 * w.y + c.Uv3 * w.z : default,
                Normal = normals ? a.Normal * w.x + b.Normal * w.y + c.Normal * w.z : default,
                Tangent = tangents ? a.Tangent * w.x + b.Tangent * w.y + c.Tangent * w.z : default,
            };
        }
    }

    private sealed class Layout
    {
        internal readonly PortraitBreakoutRegion Region;
        internal readonly PortraitFrameGeometry[] Rectangles;
        internal readonly List<int>[] Rows = Enumerable
            .Range(0, 16)
            .Select(_ => new List<int>())
            .ToArray();
        internal readonly uint[] Seen;
        internal uint Stamp;

        internal Layout(PortraitBreakoutRegion region)
        {
            Region = region;
            Rectangles = region.Rectangles();
            Seen = new uint[Rectangles.Length];
            for (var i = 0; i < Rectangles.Length; i++)
            for (var row = Row(Rectangles[i].Bottom); row <= Row(Rectangles[i].Top); row++)
                Rows[row].Add(i);
        }

        internal int Row(float v) =>
            Math.Max(0, Math.Min(15, (int)((v - Region.Uv.Bottom) * 16 / Region.Uv.Height)));
    }

    private NativePortraitLayeredRenderer(
        SkeletonAnimation skeleton,
        PortraitBreakoutPlan plan,
        PortraitComposition fit
    )
    {
        _skeleton = skeleton;
        _opening = plan.Frame;
        _fit = fit;
        _regions = plan.Regions.ToDictionary(
            r => r.Part,
            r => new Layout(r),
            StringComparer.Ordinal
        );
        _pages = NativePortraitPoseCapture
            .Pages(skeleton)
            .Select((page, i) => (page, i))
            .ToDictionary(p => p.page, p => p.i);
        _bottom = new Plane(0, fit.Scale, fit.Y - plan.Frame.Bottom - plan.Frame.Height * 0.08f);
        _object = new GameObject("BPP_PortraitRegions");
        _object.layer = skeleton.gameObject.layer;
        _object.transform.SetParent(skeleton.transform, false);
        _object.AddComponent<MeshFilter>().sharedMesh = _mesh;
        _renderer = _object.AddComponent<MeshRenderer>();
        _renderer.shadowCastingMode = ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _renderer.sortingLayerID = 0;
        _renderer.sortingOrder = 2;
        _renderer.forceRenderingOff = true;
        _ring = CreateRing(plan.Frame, fit, out var bounds);
        _ringObject = new GameObject("BPP_PortraitExteriorMask");
        _ringObject.layer = skeleton.gameObject.layer;
        _ringObject.transform.SetParent(skeleton.transform, false);
        _ringObject.transform.localPosition = bounds.center;
        _ringObject.transform.localScale = new Vector3(bounds.width, bounds.height, 1);
        var mask = _ringObject.AddComponent<SpriteMask>();
        mask.sprite = _ring;
        mask.alphaCutoff = 0.5f;
        mask.isCustomRangeActive = true;
        mask.frontSortingLayerID = mask.backSortingLayerID = 0;
        mask.frontSortingOrder = 3;
        mask.backSortingOrder = 1;
        _mesh.MarkDynamic();
        _source.MarkDynamic();
    }

    internal static NativePortraitLayeredRenderer? TryCreate(
        SkeletonAnimation skeleton,
        PortraitBreakoutPlan plan,
        PortraitComposition fit
    )
    {
        if (plan.Regions.Length == 0)
            return null;
        var view = new NativePortraitLayeredRenderer(skeleton, plan, fit);
        try
        {
            skeleton.singleSubmesh = false;
            skeleton.disableRenderingOnOverride = false;
            skeleton.GetComponent<MeshRenderer>().enabled = true;
            skeleton.updateWhenInvisible = UpdateMode.FullUpdate;
            skeleton.UpdateComplete += view.UpdateRoles;
            skeleton.GenerateMeshOverride += view.Render;
            view.UpdateRoles(skeleton);
            skeleton.LateUpdateMesh();
            if (view.Failed)
                throw new InvalidOperationException(
                    "The portrait region geometry could not be built."
                );
            return view;
        }
        catch
        {
            view.Dispose();
            throw;
        }
    }

    internal void SuppressRendering(bool suppress)
    {
        _suppressed = suppress;
        if (_renderer != null)
            _renderer.forceRenderingOff = suppress || Failed;
    }

    private static bool Drawable(Slot slot) =>
        slot.Bone.Active
        && slot.A > 0
        && slot.Attachment is RegionAttachment { A: > 0 } or MeshAttachment { A: > 0 };

    private void UpdateRoles(ISkeletonAnimation animation)
    {
        _separators.Clear();
        Layout? previous = null;
        var first = true;
        foreach (var slot in _skeleton.Skeleton.DrawOrder)
            if (Drawable(slot))
            {
                var current = Region(slot);
                if (!first && !ReferenceEquals(previous, current))
                    _separators.Add(slot);
                previous = current;
                first = false;
            }
    }

    private Layout? Region(Slot slot)
    {
        var key = (slot.Attachment, slot.Data.Index);
        if (!_attachments.TryGetValue(key, out var layout))
        {
            _regions.TryGetValue(
                NativePortraitPoseCapture.PartKey(slot.Data.Index, slot.Attachment.Name),
                out layout
            );
            _attachments.Add(key, layout);
        }
        if (layout == null)
            return null;
        var region = slot.Attachment switch
        {
            RegionAttachment r when r.Sequence == null => r.Region as AtlasRegion,
            MeshAttachment m when m.Sequence == null => m.Region as AtlasRegion,
            _ => null,
        };
        return
            region != null
            && _pages.TryGetValue(region.page, out var page)
            && page == layout.Region.Page
            ? layout
            : null;
    }

    private void Render(SkeletonRendererInstruction _)
    {
        if (_disposed || Failed)
            return;
        try
        {
            _generator.settings = new MeshGenerator.Settings
            {
                pmaVertexColors = _skeleton.pmaVertexColors,
                zSpacing = _skeleton.zSpacing,
                useClipping = _skeleton.useClipping,
                tintBlack = _skeleton.tintBlack,
                calculateTangents = _skeleton.calculateTangents,
                addNormals = _skeleton.addNormals,
            };
            MeshGenerator.GenerateSkeletonRendererInstruction(
                _instruction,
                _skeleton.Skeleton,
                _skeleton.CustomSlotMaterials,
                _separators,
                true
            );
            if (_skeleton.CustomMaterialOverride.Count != 0)
                MeshGenerator.TryReplaceMaterials(
                    _instruction.submeshInstructions,
                    _skeleton.CustomMaterialOverride
                );
            _generator.Begin();
            _items.Clear();
            for (var i = 0; i < _instruction.submeshInstructions.Count; i++)
            {
                var item = _instruction.submeshInstructions.Items[i];
                if (item.rawVertexCount == 0 || item.material == null)
                    continue;
                Layout? layout = null;
                var count = 0;
                for (var slot = item.startSlot; slot < item.endSlot; slot++)
                    if (Drawable(item.skeleton.DrawOrder.Items[slot]))
                    {
                        layout = Region(item.skeleton.DrawOrder.Items[slot]);
                        count++;
                    }
                if (count != 1 || layout == null)
                    continue;
                _generator.AddSubmesh(item, true);
                _items.Add((item, layout));
            }
            if (_items.Count == 0)
            {
                _mesh.Clear();
                return;
            }
            if (_generator.VertexCount > 30_000 || _instruction.submeshInstructions.Count > 1024)
                throw new InvalidOperationException("Portrait source geometry exceeds its limit.");
            if (_skeleton.addNormals || _skeleton.calculateTangents)
            {
                _generator.FillVertexData(_source);
                _generator.FillTriangles(_source);
                _generator.FillLateVertexData(_source);
                if (_skeleton.addNormals)
                    _source.GetNormals(_sourceNormals);
                if (_skeleton.calculateTangents)
                    _source.GetTangents(_sourceTangents);
            }
            _work = 0;
            _positions.Clear();
            _uvs.Clear();
            _colors.Clear();
            _uv2.Clear();
            _uv3.Clear();
            _normals.Clear();
            _tangents.Clear();
            _materials.Clear();
            foreach (var indices in _triangles)
                indices.Clear();
            var buffers = _generator.Buffers;
            var dark2 = _skeleton.tintBlack ? _generator.UV2 : null;
            var dark3 = _skeleton.tintBlack ? _generator.UV3 : null;
            Vertex Read(int id) =>
                new()
                {
                    Position = buffers.vertexBuffer[id],
                    Uv = buffers.uvBuffer[id],
                    Color = buffers.colorBuffer[id],
                    Uv2 = dark2?[id] ?? default,
                    Uv3 = dark3?[id] ?? default,
                    Normal = _skeleton.addNormals ? _sourceNormals[id] : default,
                    Tangent = _skeleton.calculateTangents ? _sourceTangents[id] : default,
                };
            for (var sub = 0; sub < _items.Count; sub++)
            {
                var (item, layout) = _items[sub];
                var indices = _generator.Indices.Items[sub];
                if (indices.Count == 0)
                    continue;
                var material = Masked(item.material);
                if (_materials.Count == 0 || _materials[_materials.Count - 1] != material)
                {
                    _materials.Add(material);
                    while (_triangles.Count < _materials.Count)
                        _triangles.Add(new List<int>());
                }
                _submesh = _materials.Count - 1;
                for (var i = 0; i < indices.Count; i += 3)
                {
                    if (
                        Inside(buffers.vertexBuffer[indices.Items[i]])
                        && Inside(buffers.vertexBuffer[indices.Items[i + 1]])
                        && Inside(buffers.vertexBuffer[indices.Items[i + 2]])
                    )
                        continue;
                    var a = Read(indices.Items[i]);
                    var b = Read(indices.Items[i + 1]);
                    var c = Read(indices.Items[i + 2]);
                    var template = GetTemplate(new Key(layout, a.Uv, b.Uv, c.Uv));
                    for (var vertex = 0; vertex < template.Length; vertex += 3)
                    {
                        _a[0] = Vertex.Apply(
                            template[vertex],
                            a,
                            b,
                            c,
                            _skeleton.tintBlack,
                            _skeleton.addNormals,
                            _skeleton.calculateTangents
                        );
                        _a[1] = Vertex.Apply(
                            template[vertex + 1],
                            a,
                            b,
                            c,
                            _skeleton.tintBlack,
                            _skeleton.addNormals,
                            _skeleton.calculateTangents
                        );
                        _a[2] = Vertex.Apply(
                            template[vertex + 2],
                            a,
                            b,
                            c,
                            _skeleton.tintBlack,
                            _skeleton.addNormals,
                            _skeleton.calculateTangents
                        );
                        var count = layout.Region.BottomCrop ? Clip(_a, 3, _b, _bottom, true) : 3;
                        Emit(layout.Region.BottomCrop ? _b : _a, count);
                    }
                }
            }
            _mesh.Clear();
            _mesh.SetVertices(_positions);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetColors(_colors);
            if (_skeleton.tintBlack)
            {
                _mesh.SetUVs(1, _uv2);
                _mesh.SetUVs(2, _uv3);
            }
            if (_skeleton.addNormals)
                _mesh.SetNormals(_normals);
            if (_skeleton.calculateTangents)
                _mesh.SetTangents(_tangents);
            _mesh.subMeshCount = _materials.Count;
            for (var i = 0; i < _materials.Count; i++)
                _mesh.SetTriangles(_triangles[i], i, false);
            _mesh.RecalculateBounds();
            _renderer.SetSharedMaterials(_materials);
            var main = _skeleton.GetComponent<MeshRenderer>();
            if (main.HasPropertyBlock())
            {
                main.GetPropertyBlock(_properties);
                if (_properties.HasFloat(Shader.PropertyToID("_StencilComp")))
                    throw new InvalidOperationException(
                        "Portrait property block overrides its mask."
                    );
                _renderer.SetPropertyBlock(_properties);
            }
            else
                _renderer.SetPropertyBlock(null);
            _renderer.forceRenderingOff = _suppressed;
        }
        catch (Exception ex)
        {
            Failed = true;
            _renderer.forceRenderingOff = true;
            _mesh.Clear();
            BppLog.DebugEvent(
                new BppLogEvent(BppLogFeatureScope.Plugin, "plugin.hero_portrait.mesh_failed"),
                ex,
                () => []
            );
        }
    }

    private bool Inside(Vector3 position) =>
        _opening.Contains(position.x * _fit.Scale + _fit.X, position.y * _fit.Scale + _fit.Y);

    private Weighted[] GetTemplate(Key key)
    {
        if (_cache.TryGetValue(key, out var node))
        {
            _recent.Remove(node);
            _recent.AddFirst(node);
            return node.Value.Vertices;
        }
        _weights.Clear();
        _a[0] = new Vertex { Position = Vector3.right, Uv = key.A };
        _a[1] = new Vertex { Position = Vector3.up, Uv = key.B };
        _a[2] = new Vertex { Position = Vector3.forward, Uv = key.C };
        var minX = Math.Min(key.A.x, Math.Min(key.B.x, key.C.x));
        var maxX = Math.Max(key.A.x, Math.Max(key.B.x, key.C.x));
        var minY = Math.Min(key.A.y, Math.Min(key.B.y, key.C.y));
        var maxY = Math.Max(key.A.y, Math.Max(key.B.y, key.C.y));
        var layout = key.Layout;
        if (++layout.Stamp == 0)
        {
            Array.Clear(layout.Seen, 0, layout.Seen.Length);
            layout.Stamp = 1;
        }
        for (var row = layout.Row(minY); row <= layout.Row(maxY); row++)
            foreach (var id in layout.Rows[row])
            {
                if (++_work > 500_000)
                    throw new InvalidOperationException(
                        "Portrait clipping work exceeds its limit."
                    );
                if (layout.Seen[id] == layout.Stamp)
                    continue;
                layout.Seen[id] = layout.Stamp;
                var r = layout.Rectangles[id];
                if (r.Right <= minX || r.Left >= maxX || r.Top <= minY || r.Bottom >= maxY)
                    continue;
                Array.Copy(_a, _boxA, 3);
                var count = Clip(_boxA, 3, _boxB, new Plane(1, 0, -r.Left, true), true);
                count = Clip(_boxB, count, _boxA, new Plane(-1, 0, r.Right, true), true);
                count = Clip(_boxA, count, _boxB, new Plane(0, 1, -r.Bottom, true), true);
                count = Clip(_boxB, count, _boxA, new Plane(0, -1, r.Top, true), true);
                for (var i = 1; i < count - 1; i++)
                {
                    _weights.Add(new(_boxA[0].Position, _boxA[0].Uv));
                    _weights.Add(new(_boxA[i].Position, _boxA[i].Uv));
                    _weights.Add(new(_boxA[i + 1].Position, _boxA[i + 1].Uv));
                }
            }
        var vertices = _weights.ToArray();
        // A moving native clip can keep producing new UV keys. Evict old templates;
        // cache pressure must never turn a healthy animation into a static fallback.
        if (vertices.Length <= MaxVertices)
        {
            while (_recent.Count >= 8192 || _cachedVertices + vertices.Length > MaxVertices)
            {
                var last = _recent.Last!;
                _cachedVertices -= last.Value.Vertices.Length;
                _cache.Remove(last.Value.Key);
                _recent.RemoveLast();
            }
            _cachedVertices += vertices.Length;
            _cache.Add(key, _recent.AddFirst(new Template(key, vertices)));
        }
        return vertices;
    }

    private Sprite CreateRing(PortraitFrameGeometry frame, PortraitComposition fit, out Rect bounds)
    {
        var points = new List<PortraitPoint>
        {
            new(frame.Left + frame.Bevel, frame.Bottom),
            new(frame.Right - frame.Bevel, frame.Bottom),
            new(frame.Right, frame.Bottom + frame.Bevel),
        };
        for (var i = PortraitFrameGeometry.ArchSegments; i >= 0; i--)
        {
            var x = frame.Left + frame.Width * i / PortraitFrameGeometry.ArchSegments;
            points.Add(new(x, frame.Roof(x)));
        }
        points.Add(new(frame.Left, frame.Bottom + frame.Bevel));
        var planes = points
            .Select(
                (p, i) =>
                {
                    var next = points[(i + 1) % points.Count];
                    var a = -(next.Y - p.Y);
                    var b = next.X - p.X;
                    return new Plane(
                        a * fit.Scale,
                        b * fit.Scale,
                        a * (fit.X - p.X) + b * (fit.Y - p.Y)
                    );
                }
            )
            .ToArray();
        bounds = Rect.MinMaxRect(
            (frame.Left - frame.Width * PortraitBreakoutPlan.SideLimit - fit.X) / fit.Scale,
            (frame.Bottom - fit.Y) / fit.Scale,
            (frame.Right + frame.Width * PortraitBreakoutPlan.SideLimit - fit.X) / fit.Scale,
            (frame.Top + frame.Height * PortraitBreakoutPlan.TopLimit - fit.Y) / fit.Scale
        );
        var rectangle = bounds;
        var vertices = new List<Vector2>();
        var triangles = new List<ushort>();
        void Add(Vertex[] polygon, int count)
        {
            if (count < 3)
                return;
            var start = vertices.Count;
            for (var i = 0; i < count; i++)
                vertices.Add(
                    new Vector2(
                        (polygon[i].Position.x - rectangle.xMin)
                            / rectangle.width
                            * Texture2D.whiteTexture.width,
                        (polygon[i].Position.y - rectangle.yMin)
                            / rectangle.height
                            * Texture2D.whiteTexture.height
                    )
                );
            for (var i = 1; i < count - 1; i++)
            {
                triangles.Add((ushort)start);
                triangles.Add((ushort)(start + i));
                triangles.Add((ushort)(start + i + 1));
            }
        }
        var corners = new[]
        {
            new Vector3(bounds.xMin, bounds.yMin),
            new Vector3(bounds.xMax, bounds.yMin),
            new Vector3(bounds.xMax, bounds.yMax),
            new Vector3(bounds.xMin, bounds.yMax),
        };
        foreach (var indices in new[] { new[] { 0, 1, 2 }, new[] { 2, 3, 0 } })
        {
            for (var i = 0; i < 3; i++)
                _a[i] = new Vertex { Position = corners[indices[i]] };
            var a = _a;
            var b = _b;
            var count = 3;
            foreach (var plane in planes)
            {
                Add(_outside, Clip(a, count, _outside, plane, false));
                count = Clip(a, count, b, plane, true);
                (a, b) = (b, a);
                if (count < 3)
                    break;
            }
        }
        var texture = Texture2D.whiteTexture;
        var sprite = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            texture.width
        );
        try
        {
            sprite.name = "BPP_PortraitExteriorMask";
            sprite.OverrideGeometry(vertices.ToArray(), triangles.ToArray());
            return sprite;
        }
        catch
        {
            Object.Destroy(sprite);
            throw;
        }
    }

    private int Clip(Vertex[] input, int count, Vertex[] output, Plane plane, bool inside)
    {
        if (count == 0)
            return 0;
        _work += count;
        if (_work > 500_000)
            throw new InvalidOperationException("Portrait clipping work exceeds its limit.");
        var size = 0;
        var previous = input[count - 1];
        var pd = plane.Distance(previous) * (inside ? 1 : -1);
        for (var i = 0; i < count; i++)
        {
            var current = input[i];
            var cd = plane.Distance(current) * (inside ? 1 : -1);
            if ((pd >= 0) != (cd >= 0))
                output[size++] = Vertex.Lerp(previous, current, pd / (pd - cd));
            if (cd >= 0)
                output[size++] = current;
            previous = current;
            pd = cd;
        }
        return size;
    }

    private void Emit(Vertex[] polygon, int count)
    {
        if (count < 3)
            return;
        if (_positions.Count + count > MaxVertices)
            throw new InvalidOperationException("Portrait region geometry exceeds its limit.");
        var start = _positions.Count;
        for (var i = 0; i < count; i++)
        {
            var v = polygon[i];
            _positions.Add(v.Position);
            _uvs.Add(v.Uv);
            _colors.Add(v.Color);
            if (_skeleton.tintBlack)
            {
                _uv2.Add(v.Uv2);
                _uv3.Add(v.Uv3);
            }
            if (_skeleton.addNormals)
                _normals.Add(v.Normal);
            if (_skeleton.calculateTangents)
                _tangents.Add(v.Tangent);
        }
        for (var i = 1; i < count - 1; i++)
        {
            _triangles[_submesh].Add(start);
            _triangles[_submesh].Add(start + i);
            _triangles[_submesh].Add(start + i + 1);
        }
    }

    private Material Masked(Material source)
    {
        if (_materialsBySource.TryGetValue(source, out var material))
            return material;
        if (!source.HasProperty("_StencilComp"))
            throw new InvalidOperationException("The portrait material does not support masks.");
        material = new Material(source);
        material.SetFloat("_StencilComp", (float)CompareFunction.LessEqual);
        _materialsBySource.Add(source, material);
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
            _skeleton.GenerateMeshOverride -= Render;
        }
        if (_object != null)
            Object.Destroy(_object);
        if (_ringObject != null)
            Object.Destroy(_ringObject);
        foreach (var material in _materialsBySource.Values)
            Object.Destroy(material);
        _cache.Clear();
        _recent.Clear();
        _materialsBySource.Clear();
        Object.Destroy(_ring);
        Object.Destroy(_mesh);
        Object.Destroy(_source);
    }
}
