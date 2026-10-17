#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel;

internal readonly record struct CollectionInspectionLayout(
    float CardX,
    float CardY,
    float CardWidth,
    float CardHeight,
    float DetailCenterX,
    float DetailCenterY,
    float DetailWidth,
    float DetailHeight,
    float Scale,
    float Gap
)
{
    public static CollectionInspectionLayout ForContent(
        float width,
        float height,
        float cardWidth,
        float cardHeight,
        float detailWidth,
        float detailHeight
    )
    {
        width = Math.Max(1f, width);
        height = Math.Max(1f, height);
        cardWidth = Math.Max(1f, cardWidth);
        cardHeight = Math.Max(1f, cardHeight);
        detailWidth = Math.Max(1f, detailWidth);
        detailHeight = Math.Max(1f, detailHeight);
        var gap = Math.Min(width * .025f, Math.Clamp(height * .0175f, 12f, 36f));
        var scale = Math.Min(
            1f,
            Math.Min(
                (width * .92f - gap) / (cardWidth + detailWidth),
                height * .74f / Math.Max(cardHeight, detailHeight)
            )
        );
        cardWidth *= scale;
        cardHeight *= scale;
        detailWidth *= scale;
        detailHeight *= scale;
        var left = (width - cardWidth - detailWidth - gap) / 2f;
        var cardOffset = width * .08f;
        return new(
            left + cardOffset,
            (height - cardHeight) / 2f,
            cardWidth,
            cardHeight,
            left + cardWidth + gap + detailWidth / 2f,
            height / 2f,
            detailWidth,
            detailHeight,
            scale,
            gap - cardOffset
        );
    }
}

internal readonly record struct CollectionInspectionCardPlacement(
    float X,
    float Y,
    float Width,
    float Height
)
{
    public (float X, float Y) ScaleFor(float visualWidth, float visualHeight) =>
        (Width / Math.Max(1f, visualWidth), Height / Math.Max(1f, visualHeight));
}

// Selection changes replace native previews, but never reflow the card around new text.
internal sealed class CollectionInspectionCardAnchor
{
    private CollectionInspectionCardPlacement? _placement;
    private float _width,
        _height;

    public bool NeedsLayout(float width, float height) =>
        !_placement.HasValue || _width != width || _height != height;

    public CollectionInspectionCardPlacement Resolve(
        float width,
        float height,
        CollectionInspectionLayout candidate
    )
    {
        if (NeedsLayout(width, height))
        {
            _width = width;
            _height = height;
            _placement = new(
                candidate.CardX,
                candidate.CardY,
                candidate.CardWidth,
                candidate.CardHeight
            );
        }
        return _placement!.Value;
    }

    public void Reset() => _placement = null;
}
