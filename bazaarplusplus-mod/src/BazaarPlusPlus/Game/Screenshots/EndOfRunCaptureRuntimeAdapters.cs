#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class UnityEndOfRunCaptureClock : IEndOfRunCaptureClock
{
    public float UnscaledSeconds => Time.unscaledTime;
    public float RealtimeSeconds => Time.realtimeSinceStartup;
    public long Milliseconds => (long)Math.Round(Time.realtimeSinceStartup * 1000d);
}
