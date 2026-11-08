#nullable enable
using BazaarPlusPlus.Infrastructure.HeroPortraits.Composition;

namespace BazaarPlusPlus.Game.HeroPortraits;

internal sealed record PortraitRigFamily(int Bone, int[] Ancestors)
{
    internal static PortraitRigFamily From(PortraitRigBinding binding)
    {
        var total = binding.Influences.Sum(i => i.Weight);
        var coverage = new Dictionary<int, float>();
        var depths = new Dictionary<int, int>();
        foreach (var influence in binding.Influences)
            for (var i = 0; i < influence.Ancestors.Length; i++)
            {
                var bone = influence.Ancestors[i];
                coverage.TryGetValue(bone, out var previous);
                coverage[bone] = previous + influence.Weight;
                depths[bone] = influence.Ancestors.Length - i - 1;
            }
        var canonical = 0;
        var deepest = -1;
        foreach (var pair in coverage)
            if (pair.Value >= total * 0.65f && depths[pair.Key] > deepest)
            {
                canonical = pair.Key;
                deepest = depths[pair.Key];
            }
        var chain =
            binding.Influences.Select(i => i.Ancestors).FirstOrDefault(a => a.Contains(canonical))
            ?? [];
        var offset = Array.IndexOf(chain, canonical);
        return new PortraitRigFamily(
            canonical,
            chain.Skip(Math.Max(0, offset)).TakeWhile(b => b != 0).Take(4).ToArray()
        );
    }
}
