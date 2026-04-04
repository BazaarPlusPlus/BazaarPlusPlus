#nullable enable
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.RunLogging.Models;

public sealed class RunLogOptionSnapshot
{
    public int Index { get; set; }

    public string? InstanceId { get; set; }

    public string? TemplateId { get; set; }

    public string? Name { get; set; }

    public string? Tier { get; set; }

    public string? Enchant { get; set; }

    public IList<string> Tags { get; set; } = new List<string>();

    public IDictionary<string, object?> Attributes { get; set; } =
        new Dictionary<string, object?>();
}
