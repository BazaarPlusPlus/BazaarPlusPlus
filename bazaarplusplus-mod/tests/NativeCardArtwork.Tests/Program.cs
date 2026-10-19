using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using BazaarPlusPlus.GameInterop.CardPreview;
using UnityEngine;

foreach (
    var body in new[]
    {
        (Width: 200f, Height: 1000f),
        (Width: 400f, Height: 1000f),
        (Width: 600f, Height: 1000f),
        (Width: 400f, Height: 400f),
    }
)
    RunVariantRoundTrip(body.Width, body.Height);
foreach (var aspect in new[] { .5f, 1f, 1.5f })
{
    var root = new RectTransform { Aspect = new() { aspectRatio = aspect } };
    var art = new RectTransform
    {
        Parent = root,
        StretchToParent = true,
        RequiresActivation = true,
    };
    root.Artwork = art;
    if (NativeCardPreviewArtworkMeasurement.TryMeasure(root, out _))
        throw new InvalidOperationException(
            "Uninitialized hidden artwork must not be considered ready."
        );
    art.Visible = true; // Native Show must precede the layout/measurement stage.
    if (
        !NativeCardPreviewArtworkMeasurement.PrepareLayoutSize(root, 484f)
        || !NativeCardPreviewArtworkMeasurement.TryMeasure(root, out var body)
    )
        throw new InvalidOperationException(
            "Activated zero-size preview must initialize and become measurable."
        );
    Near(484f * aspect, body.width, "initialized body width");
    Near(484f, body.height, "initialized body height");
    if (!NativeCardPreviewArtworkMeasurement.PrepareLayoutSize(root, 999f))
        throw new InvalidOperationException("Initialized root must remain ready.");
    Near(484f, root.rect.height, "valid root must not be reinitialized");
}
Console.WriteLine(
    "Tier/enchantment roundtrips preserve artwork size and center across card sizes and pooled-frame reuse."
);

static void RunVariantRoundTrip(float bodyWidth, float bodyHeight)
{
    var preview = new RectTransform
    {
        Rect = new(-bodyWidth * .2f, -bodyHeight * .9f, bodyWidth, bodyHeight),
        Position = new(500f, -300f),
        Scale = new(3f, .4f),
    };
    var artwork = new RectTransform
    {
        Parent = preview,
        Rect = new(-bodyWidth / 2f, -bodyHeight / 2f, bodyWidth, bodyHeight),
        Position = new(37f, -23f),
        Visible = false,
    };
    preview.Artwork = artwork;
    var variants = new[]
    {
        (Name: "initial", Width: 1.1f, Height: 1.06f, DescriptionHeight: 400f),
        (Name: "higher-tier-wide", Width: 1.5f, Height: 1.15f, DescriptionHeight: 900f),
        (Name: "higher-tier-tall", Width: 1.25f, Height: 1.3f, DescriptionHeight: 1800f),
        (Name: "lower-tier-again", Width: 1.1f, Height: 1.06f, DescriptionHeight: 400f),
        (Name: "enchantment-wide", Width: 1.9f, Height: 1.1f, DescriptionHeight: 2400f),
        (Name: "enchantment-tall", Width: 1.15f, Height: 1.5f, DescriptionHeight: 1800f),
        (Name: "higher-tier-and-enchantment", Width: 2f, Height: 1.6f, DescriptionHeight: 2400f),
        (Name: "reset-tier-and-enchantment", Width: 1.1f, Height: 1.06f, DescriptionHeight: 400f),
    };
    var anchor = new CollectionInspectionCardAnchor();
    CollectionInspectionCardPlacement? initial = null;
    foreach (var variant in variants)
    {
        foreach (var oldFrame in preview.Frames)
            oldFrame.Visible = false;
        var frame = new RectTransform
        {
            Parent = preview,
            Rect = new(
                -bodyWidth * variant.Width / 2f,
                -bodyHeight * variant.Height / 2f,
                bodyWidth * variant.Width,
                bodyHeight * variant.Height
            ),
            Visible = true,
        };
        preview.Frames.Add(frame);
        artwork.Visible = false; // Native SetUp conceals artwork until the owner fits it.
        if (!NativeCardPreviewArtworkMeasurement.TryMeasure(preview, out var measured))
            throw new InvalidOperationException(
                variant.Name + ": hidden artwork was not measurable."
            );
        Near(bodyWidth, measured.width, "body width excludes all tier/enchantment frames");
        Near(bodyHeight, measured.height, "body height excludes all tier/enchantment frames");
        Near(37f, measured.center.x, "body center X");
        Near(-23f, measured.center.y, "body center Y");
        var candidate = CollectionInspectionLayout.ForContent(
            2560f,
            1600f,
            measured.width,
            measured.height,
            800f,
            variant.DescriptionHeight
        );
        var placement = anchor.Resolve(2560f, 1600f, candidate);
        initial ??= placement;
        var scale = placement.ScaleFor(measured.width, measured.height);
        var bounds = new CardVisualBounds(
            measured.width,
            measured.height,
            measured.center.x,
            measured.center.y
        );
        var target = new CollectionGridRect(
            placement.X,
            placement.Y,
            placement.Width,
            placement.Height
        );
        var position = CollectionCardFitMath.ComputeAnchoredPosition(
            bounds,
            scale.X,
            scale.Y,
            target,
            0f
        );
        preview.Scale = new(scale.X, scale.Y);
        preview.Position = new(position.X, position.Y);
        artwork.Visible = true;
        var corners = new Vector3[4];
        artwork.GetWorldCorners(corners);
        Near(initial.Value.Width, corners[2].x - corners[0].x, variant.Name + " actual body width");
        Near(
            initial.Value.Height,
            corners[2].y - corners[0].y,
            variant.Name + " actual body height"
        );
        Near(
            initial.Value.X + initial.Value.Width / 2f,
            (corners[2].x + corners[0].x) / 2f,
            variant.Name + " center X"
        );
        Near(
            -(initial.Value.Y + initial.Value.Height / 2f),
            (corners[2].y + corners[0].y) / 2f,
            variant.Name + " center Y"
        );
        if (preview.Frames.Count > 1)
        {
            // An old pooled frame can be active and reparented to a different card.
            var borrowed = preview.Frames[^2];
            borrowed.Parent = new RectTransform { Scale = new(20f, 20f) };
            borrowed.Visible = true;
        }
    }
    preview.Artwork = null;
    if (NativeCardPreviewArtworkMeasurement.TryMeasure(preview, out _))
        throw new InvalidOperationException(
            "Missing artwork must not fall back to a pooled frame."
        );
    preview.Artwork = new RectTransform { Parent = preview, Rect = new(0f, 0f, 0f, bodyHeight) };
    if (NativeCardPreviewArtworkMeasurement.TryMeasure(preview, out _))
        throw new InvalidOperationException("Unready artwork must not produce a fit.");
}

static void Near(float expected, float actual, string label)
{
    if (Math.Abs(expected - actual) > .001f)
        throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}");
}
