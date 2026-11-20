#nullable enable
using Spine;

namespace BazaarPlusPlus.Game.HeroPortraits;

internal sealed record PortraitBreakoutSampling(float[] Times, HashSet<int> StrictSlots)
{
    internal static PortraitBreakoutSampling? Create(Spine.Animation animation, float poseTime)
    {
        var times = new SortedSet<float> { 0, poseTime, animation.Duration };
        for (var i = 1; i < 32; i++)
            times.Add(animation.Duration * i / 32);

        // Renderer-wide order changes get sample budget before individual attachment changes.
        foreach (var timeline in animation.Timelines.OfType<DrawOrderTimeline>())
        {
            var orders = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < timeline.FrameCount; i++)
            {
                var order = timeline.DrawOrders[i];
                var key = order == null ? "setup" : string.Join(",", order);
                if (orders.Add(key))
                    AddBoundary(times, timeline.Frames[i]);
                if (times.Count > 64)
                    return null;
            }
        }

        var strict = new HashSet<int>();
        foreach (var timeline in animation.Timelines.OfType<AttachmentTimeline>())
        {
            var proposed = new SortedSet<float>(times);
            var transitions = new HashSet<(bool Initial, string? Before, string? After)>();
            for (var i = 0; i < timeline.FrameCount; i++)
            {
                var before = i == 0 ? null : timeline.AttachmentNames[i - 1];
                var after = timeline.AttachmentNames[i];
                // Sample each transition once even when the flipbook repeats many times.
                if (transitions.Add((i == 0, before, after)))
                    AddBoundary(proposed, timeline.Frames[i]);
                if (proposed.Count > 64)
                    break;
            }
            if (proposed.Count > 64)
                strict.Add(timeline.SlotIndex);
            else
                times = proposed;
        }
        return new PortraitBreakoutSampling(times.ToArray(), strict);
    }

    internal bool Allows(string? part)
    {
        if (StrictSlots.Count == 0)
            return true;
        var separator = part?.IndexOf(':') ?? -1;
        return separator > 0
            && int.TryParse(part!.Substring(0, separator), out var slot)
            && !StrictSlots.Contains(slot);
    }

    private static void AddBoundary(SortedSet<float> times, float time)
    {
        times.Add(time);
        times.Add(Math.Max(0, time - 0.001f));
    }
}
