#nullable enable

namespace BazaarPlusPlus.Core.Cards;

/// <summary>
/// The game's card UI prefab as BPP measures it, the one owner every layout that places a native
/// card reads: the desktop Collection grid, and the Android card rental, Collection, History, and
/// Ten-Win Builds panels. A card set up unscaled is <see cref="Height"/> high in its socket, an
/// item as wide as its size makes it and a skill square, and its frame art stands
/// <see cref="FrameHeightOverSocket"/> times taller than the socket.
/// </summary>
public static class NativeCardPrefab
{
    /// <summary>An unscaled card's height in its socket; a skill is as wide.</summary>
    public const float Height = 484f;

    /// <summary>One socket: a small item's width at <see cref="Height"/>.</summary>
    public const float SocketWidth = 252f;

    /// <summary>A medium item's width at <see cref="Height"/>.</summary>
    public const float MediumWidth = 503f;

    /// <summary>A large item's width at <see cref="Height"/>.</summary>
    public const float LargeWidth = 755f;

    /// <summary>
    /// The frame's height over its socket's, as the board carpets measure it: a card's frame
    /// overhangs its body, so fit math divides it out to size the body to the cell.
    /// </summary>
    public const float FrameHeightOverSocket = 1.03704f;

    /// <summary>An item's unscaled width for its size; an unknown size reads as medium.</summary>
    public static float ItemWidth(CardSize size) =>
        size switch
        {
            CardSize.Small => SocketWidth,
            CardSize.Large => LargeWidth,
            _ => MediumWidth,
        };
}
