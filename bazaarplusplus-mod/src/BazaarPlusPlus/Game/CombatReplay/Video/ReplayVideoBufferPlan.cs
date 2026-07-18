#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal sealed class ReplayVideoBufferPlan
{
    internal const long DefaultPoolBudgetBytes = 128L * 1024 * 1024;

    private ReplayVideoBufferPlan(
        int frameByteLength,
        int poolCapacity,
        int queueCapacity,
        long poolPayloadBytes,
        bool budgetExceeded
    )
    {
        FrameByteLength = frameByteLength;
        PoolCapacity = poolCapacity;
        QueueCapacity = queueCapacity;
        PoolPayloadBytes = poolPayloadBytes;
        BudgetExceeded = budgetExceeded;
    }

    internal int FrameByteLength { get; }
    internal int PoolCapacity { get; }
    internal int QueueCapacity { get; }
    internal long PoolPayloadBytes { get; }
    internal bool BudgetExceeded { get; }

    internal static ReplayVideoBufferPlan Create(
        int width,
        int height,
        long poolBudgetBytes = DefaultPoolBudgetBytes
    )
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Video width must be positive.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Video height must be positive.");
        if (poolBudgetBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(poolBudgetBytes));

        var frameByteLengthLong = checked((long)width * height * 4L);
        if (frameByteLengthLong > int.MaxValue)
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "A raw RGBA frame exceeds the supported byte-array size."
            );

        var budgetCapacity = (int)Math.Min(int.MaxValue, poolBudgetBytes / frameByteLengthLong);
        var poolCapacity = Math.Max(3, Math.Min(12, budgetCapacity));
        var queueCapacity = Math.Max(1, poolCapacity - 1);
        var poolPayloadBytes = checked(frameByteLengthLong * poolCapacity);

        return new ReplayVideoBufferPlan(
            (int)frameByteLengthLong,
            poolCapacity,
            queueCapacity,
            poolPayloadBytes,
            poolPayloadBytes > poolBudgetBytes
        );
    }
}
