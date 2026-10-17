namespace UnityEngine
{
    internal readonly record struct Vector2(float x, float y);

    internal readonly record struct Vector3(float x, float y, float z = 0f);

    internal readonly record struct Rect(float x, float y, float width, float height)
    {
        public Vector2 center => new(x + width / 2f, y + height / 2f);

        public static Rect MinMaxRect(float minX, float minY, float maxX, float maxY) =>
            new(minX, minY, maxX - minX, maxY - minY);
    }

    // An affine geometry shim; the production measurement and fit math are compiled unchanged.
    internal sealed class RectTransform
    {
        public enum Axis
        {
            Horizontal,
            Vertical,
        }

        public Rect Rect { get; set; }
        public Rect rect =>
            RequiresActivation && !Visible ? default
            : StretchToParent && Parent != null
                ? new Rect(
                    -Parent.rect.width / 2f,
                    -Parent.rect.height / 2f,
                    Parent.rect.width,
                    Parent.rect.height
                )
            : Rect;
        public bool StretchToParent { get; set; }
        public bool RequiresActivation { get; set; }
        public GameObject gameObject { get; } = new();
        public UI.AspectRatioFitter? Aspect { get; set; }

        public T? GetComponent<T>()
            where T : class => Aspect as T;

        public void SetSizeWithCurrentAnchors(Axis axis, float size) =>
            Rect =
                axis == Axis.Horizontal ? Rect with { width = size } : Rect with { height = size };

        public RectTransform? Parent { get; set; }
        public RectTransform? Artwork { get; set; }
        public List<RectTransform> Frames { get; } = new();
        public Vector3 Position { get; set; }
        public Vector3 Scale { get; set; } = new(1f, 1f, 1f);
        public bool Visible { get; set; }

        public Vector3 TransformPoint(Vector3 point)
        {
            var local = new Vector3(point.x * Scale.x + Position.x, point.y * Scale.y + Position.y);
            return Parent == null ? local : Parent.TransformPoint(local);
        }

        public Vector3 InverseTransformPoint(Vector3 point)
        {
            var parent = Parent == null ? point : Parent.InverseTransformPoint(point);
            return new((parent.x - Position.x) / Scale.x, (parent.y - Position.y) / Scale.y);
        }

        public void GetWorldCorners(Vector3[] corners)
        {
            var measured = rect;
            corners[0] = TransformPoint(new(measured.x, measured.y));
            corners[1] = TransformPoint(new(measured.x, measured.y + measured.height));
            corners[2] = TransformPoint(
                new(measured.x + measured.width, measured.y + measured.height)
            );
            corners[3] = TransformPoint(new(measured.x + measured.width, measured.y));
        }
    }

    internal sealed class GameObject
    {
        public bool activeSelf { get; set; }
        public bool activeInHierarchy { get; set; }
    }
}

namespace UnityEngine.UI
{
    internal sealed class AspectRatioFitter
    {
        public float aspectRatio { get; set; }
        public string aspectMode { get; set; } = "HeightControlsWidth";
    }
}

namespace BazaarPlusPlus.GameInterop.CardPreview
{
    internal static class NativeCardPreviewReflection
    {
        // Models the exact _cardImage reference seam, not a subtree or frame lookup.
        public static bool TryGetArtworkRect(
            UnityEngine.RectTransform preview,
            out UnityEngine.RectTransform artwork,
            bool requireVisible = true
        )
        {
            artwork = preview.Artwork!;
            return artwork != null && (!requireVisible || artwork.Visible);
        }
    }
}
