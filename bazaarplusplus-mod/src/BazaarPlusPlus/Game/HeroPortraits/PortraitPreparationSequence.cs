#nullable enable

namespace BazaarPlusPlus.Game.HeroPortraits;

internal static class PortraitPreparationSequence
{
    internal static async Task<(TComposition Composition, TFrame Frame, TPlan Plan)?> RunAsync<
        TComposition,
        TFrame,
        TPlan
    >(
        Func<Task<TComposition?>> match,
        Func<TFrame> readFrame,
        Func<TComposition, TFrame, Task<TPlan>> prepareBreakout,
        Action checkCurrent
    )
        where TComposition : class
    {
        var composition = await match();
        checkCurrent();
        if (composition == null)
            return null;
        var frame = readFrame();
        var plan = await prepareBreakout(composition, frame);
        checkCurrent();
        return (composition, frame, plan);
    }
}
