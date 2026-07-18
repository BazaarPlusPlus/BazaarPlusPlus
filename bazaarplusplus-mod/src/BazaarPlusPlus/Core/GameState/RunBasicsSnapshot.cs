#nullable enable
namespace BazaarPlusPlus.Core.GameState;

internal sealed class RunBasicsSnapshot
{
    public int? Day { get; set; }

    public int? Hour { get; set; }

    public int? Victories { get; set; }

    public int? Losses { get; set; }

    public string? Hero { get; set; }

    public string? GameMode { get; set; }
}
