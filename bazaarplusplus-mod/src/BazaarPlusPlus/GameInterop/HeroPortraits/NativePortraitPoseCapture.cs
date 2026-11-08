#nullable enable
using System.Globalization;
using BazaarPlusPlus.Infrastructure.HeroPortraits.Composition;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.HeroPortraits;

internal static class NativePortraitPoseCapture
{
    internal static Skeleton CreateSkeleton(SkeletonData data, SkeletonAnimation source)
    {
        var skeleton = new Skeleton(data)
        {
            ScaleX = source.initialFlipX ? -1f : 1f,
            ScaleY = source.initialFlipY ? -1f : 1f,
        };
        if (
            !string.IsNullOrEmpty(source.initialSkinName)
            && data.FindSkin(source.initialSkinName) is Skin skin
        )
            skeleton.SetSkin(skin);
        return skeleton;
    }

    internal static List<AtlasPage> Pages(SkeletonAnimation source) =>
        source
            .skeletonDataAsset.atlasAssets.SelectMany(a => a.GetAtlas().Pages)
            .Distinct()
            .ToList();

    internal static async Task<PortraitCapturedImages> CaptureImagesAsync(
        Sprite sprite,
        SkeletonAnimation source,
        CancellationToken cancellation
    )
    {
        if (sprite.packed)
            throw new NotSupportedException("Packed portrait images need reconstruction.");
        var pages = Pages(source);
        if (pages.Count is 0 or > 4)
            throw new NotSupportedException("Unsupported portrait atlas page count.");
        var reference = await NativePortraitTexture.CaptureAsync(sprite.texture, sprite.rect, 256);
        cancellation.ThrowIfCancellationRequested();
        var images = new PortraitImage[pages.Count];
        for (var i = 0; i < pages.Count; i++)
        {
            if (
                pages[i].rendererObject is not Material material
                || material == null
                || material.mainTexture == null
            )
                throw new InvalidOperationException("The portrait has no atlas texture.");
            var texture = material.mainTexture;
            images[i] = await NativePortraitTexture.CaptureAsync(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                512
            );
            cancellation.ThrowIfCancellationRequested();
        }
        return new PortraitCapturedImages(reference, images);
    }

    internal static async Task<PortraitPose[]> CaptureAsync(
        SkeletonAnimation source,
        Spine.Animation animation,
        float[] times,
        CancellationToken cancellation,
        bool breakout
    )
    {
        var skeleton = CreateSkeleton(source.skeletonDataAsset.GetSkeletonData(false), source);
        var pages = Pages(source);
        var poses = new PortraitPose[times.Length];
        var total = 0;
        var bindings = new Dictionary<(Attachment, int), PortraitRigBinding>();
        for (var i = 0; i < times.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            skeleton.SetToSetupPose();
            var state = new Spine.AnimationState(new AnimationStateData(skeleton.Data));
            state.SetAnimation(0, animation, false).TrackTime = times[i];
            state.Apply(skeleton);
            skeleton.UpdateWorldTransform(Skeleton.Physics.None);
            poses[i] = new PortraitPose(
                times[i],
                CaptureTriangles(skeleton, pages, breakout, source.useClipping, bindings),
                DrawOrder: breakout ? CaptureDrawOrder(skeleton) : null
            );
            total += poses[i].Triangles.Length;
            if (breakout && (poses[i].Triangles.Length > 10_000 || total > 250_000))
                return [new PortraitPose(0, [], false)];
            await Task.Yield();
        }
        return poses;
    }

    internal static PortraitTriangle[] CaptureTriangles(
        Skeleton skeleton,
        List<AtlasPage> pages,
        bool breakout,
        bool useClipping = true,
        Dictionary<(Attachment, int), PortraitRigBinding>? bindings = null
    )
    {
        var clipper = breakout && useClipping ? new SkeletonClipping() : null;
        var triangles = new List<PortraitTriangle>();
        foreach (var slot in skeleton.DrawOrder)
        {
            if (
                slot.Bone.Active
                && slot.Attachment is ClippingAttachment clipping
                && clipper != null
            )
            {
                // Like MeshGenerator, the start slot does not end its own clipping range.
                clipper.ClipStart(slot, clipping);
                continue;
            }
            try
            {
                if (!slot.Bone.Active || slot.A <= 0 || (!breakout && slot.A < 0.5f))
                    continue;
                float[] vertices,
                    uv;
                int[] indices;
                TextureRegion region;
                float alpha,
                    r,
                    g,
                    b;
                if (slot.Attachment is RegionAttachment attachment)
                {
                    alpha = attachment.A;
                    r = attachment.R;
                    g = attachment.G;
                    b = attachment.B;
                    vertices = new float[8];
                    attachment.ComputeWorldVertices(slot, vertices, 0);
                    uv = attachment.UVs;
                    region = attachment.Region;
                    indices = [0, 1, 2, 2, 3, 0];
                }
                else if (slot.Attachment is MeshAttachment mesh)
                {
                    alpha = mesh.A;
                    r = mesh.R;
                    g = mesh.G;
                    b = mesh.B;
                    vertices = new float[mesh.WorldVerticesLength];
                    mesh.ComputeWorldVertices(slot, 0, vertices.Length, vertices, 0, 2);
                    uv = mesh.UVs;
                    region = mesh.Region;
                    indices = mesh.Triangles;
                }
                else
                    continue;
                if (
                    alpha <= 0
                    || (!breakout && alpha < 0.5f)
                    || region is not AtlasRegion atlasRegion
                )
                    continue;
                var page = pages.IndexOf(atlasRegion.page);
                if (page < 0)
                    continue;
                var eligible =
                    slot.Data.BlendMode == Spine.BlendMode.Normal
                    && (
                        slot.Attachment
                        is RegionAttachment { Sequence: null }
                            or MeshAttachment { Sequence: null }
                    )
                    && slot.A * alpha >= 0.98f
                    && Math.Abs(slot.R * r - 1) < 0.01f
                    && Math.Abs(slot.G * g - 1) < 0.01f
                    && Math.Abs(slot.B * b - 1) < 0.01f;
                var part = PartKey(slot.Data.Index, slot.Attachment.Name);
                PortraitRigBinding? rig = null;
                if (breakout)
                {
                    var key = (slot.Attachment, slot.Data.Index);
                    if (bindings == null || !bindings.TryGetValue(key, out rig))
                    {
                        rig = Bind(slot, skeleton.Data);
                        bindings?.Add(key, rig);
                    }
                }
                var indexCount = indices.Length;
                if (clipper?.IsClipping == true)
                {
                    clipper.ClipTriangles(vertices, indices, indexCount, uv);
                    vertices = clipper.ClippedVertices.Items;
                    uv = clipper.ClippedUVs.Items;
                    indices = clipper.ClippedTriangles.Items;
                    indexCount = clipper.ClippedTriangles.Count;
                }
                for (var i = 0; i < indexCount; i += 3)
                {
                    var a = indices[i] * 2;
                    var bi = indices[i + 1] * 2;
                    var c = indices[i + 2] * 2;
                    triangles.Add(
                        new PortraitTriangle(
                            page,
                            new PortraitPoint(vertices[a], vertices[a + 1]),
                            new PortraitPoint(vertices[bi], vertices[bi + 1]),
                            new PortraitPoint(vertices[c], vertices[c + 1]),
                            new PortraitPoint(uv[a], uv[a + 1]),
                            new PortraitPoint(uv[bi], uv[bi + 1]),
                            new PortraitPoint(uv[c], uv[c + 1]),
                            part,
                            slot.A * alpha,
                            eligible,
                            rig
                        )
                    );
                }
            }
            finally
            {
                // End slots still terminate a clip when inactive, transparent or untextured.
                clipper?.ClipEnd(slot);
            }
        }
        clipper?.ClipEnd();
        return triangles.ToArray();
    }

    internal static string[] CaptureDrawOrder(Skeleton skeleton) =>
        skeleton
            .DrawOrder.Where(s =>
                s.Bone.Active
                && s.A > 0
                && s.Attachment is RegionAttachment { A: > 0 } or MeshAttachment { A: > 0 }
            )
            .Select(s => PartKey(s.Data.Index, s.Attachment.Name))
            .ToArray();

    private static PortraitRigBinding Bind(Slot slot, SkeletonData data)
    {
        var weights = new Dictionary<int, float>();
        if (slot.Attachment is VertexAttachment { Bones: not null } mesh)
        {
            var vertex = 0;
            for (var i = 0; i < mesh.Bones.Length; )
            {
                var count = mesh.Bones[i++];
                for (var j = 0; j < count; j++)
                {
                    var bone = mesh.Bones[i++];
                    var weight = mesh.Vertices[vertex + 2];
                    vertex += 3;
                    weights.TryGetValue(bone, out var previous);
                    weights[bone] = previous + weight;
                }
            }
        }
        else
            weights[slot.Bone.Data.Index] = 1;
        var influences = new List<PortraitBoneInfluence>();
        foreach (var influence in weights)
        {
            var ancestors = new List<int>();
            for (var bone = data.Bones.Items[influence.Key]; bone != null; bone = bone.Parent)
                ancestors.Add(bone.Index);
            influences.Add(new PortraitBoneInfluence(influence.Value, ancestors.ToArray()));
        }
        return new PortraitRigBinding(influences.ToArray());
    }

    internal static string PartKey(int slot, string attachment) =>
        slot.ToString(CultureInfo.InvariantCulture) + ":" + attachment;
}
