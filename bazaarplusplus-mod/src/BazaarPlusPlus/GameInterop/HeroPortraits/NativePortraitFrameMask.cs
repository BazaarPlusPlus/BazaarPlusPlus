#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.HeroPortraits;

internal static class NativePortraitFrameMask
{
    // Native frame meshes are not CPU-readable. Their bounds are available, so generate
    // our own silhouette in the portrait plane instead of copying the game's mesh vertices.
    internal static Rect ReadOpening(SpriteRenderer portrait)
    {
        var encounter = portrait.GetComponentInParent<EncounterController>();
        var background = encounter != null ? encounter.backgroundRenderer : null;
        if (
            background == null
            || !background.TryGetComponent<MeshFilter>(out var filter)
            || filter.sharedMesh == null
        )
            throw new InvalidOperationException("The hero portrait has no frame background mesh.");
        var bounds = filter.sharedMesh.bounds;
        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (var i = 0; i < 8; i++)
        {
            var corner =
                bounds.center
                + Vector3.Scale(
                    bounds.extents,
                    new Vector3(
                        (i & 1) == 0 ? -1f : 1f,
                        (i & 2) == 0 ? -1f : 1f,
                        (i & 4) == 0 ? -1f : 1f
                    )
                );
            var local = portrait.transform.InverseTransformPoint(
                filter.transform.TransformPoint(corner)
            );
            min = Vector2.Min(min, new Vector2(local.x, local.y));
            max = Vector2.Max(max, new Vector2(local.x, local.y));
        }
        if (max.x <= min.x || max.y <= min.y)
            throw new InvalidOperationException("The hero portrait frame has no usable opening.");
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    internal static Sprite Create(Rect opening, float top, out Rect maskBounds)
    {
        // Keep the exact opening sides and floor, with a tiny cut at the lower corners.
        // The crown rises above the opening to preserve the game's out-of-frame composition.
        top = Mathf.Max(top, opening.yMax);
        maskBounds = Rect.MinMaxRect(opening.xMin, opening.yMin, opening.xMax, top);
        var archRise = opening.height * 0.15f;
        var bevel = Mathf.Min(opening.width, opening.height) * 0.007f;
        const int archSegments = 24;
        var points = new List<Vector2>
        {
            maskBounds.center,
            new(opening.xMin + bevel, opening.yMin),
            new(opening.xMin, opening.yMin + bevel),
        };
        for (var i = 0; i <= archSegments; i++)
        {
            var t = i / (float)archSegments;
            var x = t * 2f - 1f;
            points.Add(
                new Vector2(Mathf.Lerp(opening.xMin, opening.xMax, t), top - archRise * x * x)
            );
        }
        points.Add(new Vector2(opening.xMax, opening.yMin + bevel));
        points.Add(new Vector2(opening.xMax - bevel, opening.yMin));
        var texture = Texture2D.whiteTexture;
        var vertices = new Vector2[points.Count];
        for (var i = 0; i < points.Count; i++)
            // OverrideGeometry takes pixels in Sprite.rect, not centered Unity units.
            vertices[i] = new Vector2(
                (points[i].x - maskBounds.xMin) / maskBounds.width * texture.width,
                (points[i].y - maskBounds.yMin) / maskBounds.height * texture.height
            );
        var triangles = new ushort[(vertices.Length - 1) * 3];
        for (var i = 1; i < vertices.Length; i++)
        {
            var index = (i - 1) * 3;
            triangles[index] = 0;
            triangles[index + 1] = (ushort)i;
            triangles[index + 2] = (ushort)(i == vertices.Length - 1 ? 1 : i + 1);
        }
        var sprite = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            texture.width
        );
        try
        {
            sprite.name = "BPP_HeroPortraitFrameMask";
            sprite.OverrideGeometry(vertices, triangles);
            return sprite;
        }
        catch
        {
            UnityEngine.Object.Destroy(sprite);
            throw;
        }
    }
}
