namespace BazaarPlusPlus.Game.MonsterPreview;

internal static class NextClickCloseFrameGate
{
    public static bool CanConsume(int armedFrame, int currentFrame)
    {
        return currentFrame > armedFrame;
    }
}
