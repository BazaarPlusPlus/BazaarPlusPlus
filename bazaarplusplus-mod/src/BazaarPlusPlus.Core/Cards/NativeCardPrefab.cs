#nullable enable

namespace BazaarPlusPlus.Core.Cards;

// These dimensions are measured from the native card UI prefab at its unscaled size.
public static class NativeCardPrefab
{
    public const float Height = 484f;

    public const float SocketWidth = 252f;

    public const float MediumWidth = 503f;

    public const float LargeWidth = 755f;

    // The native frame extends beyond the socket, so fit the card body separately from its visible frame.
    public const float FrameHeightOverSocket = 1.03704f;

    public static float ItemWidth(CardSize size) =>
        size switch
        {
            CardSize.Small => SocketWidth,
            CardSize.Large => LargeWidth,
            _ => MediumWidth,
        };
}
