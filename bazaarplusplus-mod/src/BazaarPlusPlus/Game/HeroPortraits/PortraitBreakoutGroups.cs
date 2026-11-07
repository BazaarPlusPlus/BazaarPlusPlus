#nullable enable
using BazaarPlusPlus.Infrastructure.HeroPortraits.Composition;

namespace BazaarPlusPlus.Game.HeroPortraits;

internal sealed record PortraitRigFamily(int Bone, int[] Ancestors);

internal sealed class PortraitBreakoutPart
{
    internal string Name { get; }
    internal PortraitRigFamily? Rig { get; }
    internal int ReferencePixels;
    internal int ReferenceMatches;
    internal int ReferenceComparable;
    internal int ReferenceOutside;
    internal int OutsideMatches;
    internal int OutsideComparable;
    internal bool Rejected;
    internal bool Contradicted;
    internal bool Truncated;
    internal bool[]? Exterior;
    internal int[] Areas { get; }

    internal PortraitBreakoutPart(string name, PortraitRigBinding? rig, int poses)
    {
        Name = name;
        Rig = rig == null ? null : PortraitBreakoutGroups.Family(rig);
        Areas = new int[poses];
    }

    internal bool Supported =>
        ReferenceMatches >= Math.Max(2, ReferencePixels * 0.2f)
        && ReferenceMatches >= ReferenceComparable * 0.65f;
}

internal static class PortraitBreakoutGroups
{
    private sealed record Anchor(int Part, int Pixels, int Comparable, int Matches)
    {
        internal bool Supported =>
            Matches >= Math.Max(2, Pixels * 0.2f) && Matches >= Comparable * 0.65f;
    }

    internal static PortraitRigFamily Family(PortraitRigBinding binding)
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

    // Pixel contact establishes visual continuity. Bone ancestry restricts it to a local
    // assembly, rather than transitively joining everything under a shared torso/root.
    internal static void AddContacts(
        int[] owners,
        int width,
        int height,
        HashSet<int> contacts,
        bool reference
    )
    {
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var a = owners[y * width + x];
            if (a < 0)
                continue;
            Add(x + 1, y);
            Add(x, y + 1);
            if (reference)
            {
                Add(x + 2, y);
                Add(x, y + 2);
                Add(x + 1, y + 1);
                Add(x - 1, y + 1);
            }
            void Add(int nx, int ny)
            {
                if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                    return;
                var b = owners[ny * width + nx];
                if (b >= 0 && a != b)
                    contacts.Add(Math.Min(a, b) * 512 + Math.Max(a, b));
            }
        }
    }

    internal static int[][] Build(
        PortraitBreakoutPart[] parts,
        HashSet<int> contacts,
        int framePixels
    )
    {
        var components = Enumerable.Range(0, parts.Length).ToArray();
        foreach (var pair in contacts)
        {
            var a = pair / 512;
            var b = pair % 512;
            if (!parts[a].Rejected && !parts[b].Rejected)
                components[Root(a)] = Root(b);
        }
        int Root(int id)
        {
            while (components[id] != id)
            {
                components[id] = components[components[id]];
                id = components[id];
            }
            return id;
        }
        // Establish indivisible local rig families before applying reference rejection.
        // A contradictory face of a rigid object must not leave its other face popping
        // through a different mask. Unknown canvas-cropped faces may borrow evidence.
        foreach (
            var family in Enumerable
                .Range(0, parts.Length)
                .Where(i => !parts[i].Rejected && parts[i].Rig is { Bone: > 0 })
                .GroupBy(i => (Root(i), parts[i].Rig!.Bone))
        )
            if (family.Any(i => parts[i].Contradicted))
                foreach (var id in family)
                    parts[id].Rejected = true;
        foreach (var part in parts)
            part.Rejected |= part.Contradicted;
        var minimumAnchor = Math.Max(12, framePixels * 0.012f);
        var anchors = Enumerable
            .Range(0, parts.Length)
            .Where(i => !parts[i].Rejected && parts[i].Rig is { Bone: > 0 })
            .GroupBy(i => (Root(i), parts[i].Rig!.Bone))
            .Select(g => new Anchor(
                g.OrderByDescending(i => parts[i].ReferenceMatches).ThenBy(i => i).First(),
                g.Sum(i => parts[i].ReferencePixels),
                g.Sum(i => parts[i].ReferenceComparable),
                g.Sum(i => parts[i].ReferenceMatches)
            ))
            .Where(a => a.Pixels >= minimumAnchor && a.Supported)
            .ToArray();
        var groups = new Dictionary<int, List<int>>();
        for (var id = 0; id < parts.Length; id++)
        {
            var part = parts[id];
            if (part.Rejected)
                continue;
            var best = part.Supported ? id : -1;
            var score = -1f;
            if (part.Rig is { Bone: > 0 } rig)
            {
                // Several small surfaces can jointly verify an independent object even
                // when no individual surface is large enough to anchor its glow or trim.
                var own = anchors.FirstOrDefault(a =>
                    Root(a.Part) == Root(id) && parts[a.Part].Rig!.Bone == rig.Bone
                );
                if (own != null)
                {
                    best = own.Part;
                    score = own.Matches;
                }
                foreach (var anchor in anchors)
                {
                    var depth = Array.IndexOf(rig.Ancestors, parts[anchor.Part].Rig!.Bone);
                    if (depth < 0)
                        continue;
                    var tinyConnector =
                        depth <= 1 && part.ReferencePixels < 12 && part.ReferenceMatches > 0;
                    var localExtension =
                        depth == 1
                        && part.ReferenceOutside > part.ReferencePixels * 0.5f
                        && part.ReferencePixels < framePixels * 0.06f;
                    // Prefer verified local identity over remote character controls.
                    // Direct-parent extensions retain their local whole-object relation.
                    if (own != null && anchor != own && !localExtension)
                        continue;
                    if (
                        Root(id) != Root(anchor.Part)
                        && rig.Bone != parts[anchor.Part].Rig!.Bone
                        && !tinyConnector
                    )
                        continue;
                    // Substantial surfaces remain their own anchors; small fingers, trim
                    // and tips borrow evidence from the largest surface in their rig family.
                    if (
                        part.ReferencePixels >= minimumAnchor
                        && parts[anchor.Part].Rig!.Bone != rig.Bone
                        && !localExtension
                    )
                        continue;
                    var value = anchor.Matches * MathF.Pow(0.25f, depth);
                    if (value > score)
                    {
                        score = value;
                        best = anchor.Part;
                    }
                }
            }
            if (best < 0)
                continue;
            if (!groups.TryGetValue(best, out var members))
                groups[best] = members = new List<int>();
            members.Add(id);
        }
        var result = groups.Values.ToArray();
        var groupIds = Enumerable.Range(0, result.Length).ToArray();
        var membership = Enumerable.Repeat(-1, parts.Length).ToArray();
        for (var i = 0; i < result.Length; i++)
            foreach (var id in result[i])
                membership[id] = i;
        int GroupRoot(int id)
        {
            while (groupIds[id] != id)
            {
                groupIds[id] = groupIds[groupIds[id]];
                id = groupIds[id];
            }
            return id;
        }
        // A small extension can borrow a parent anchor while another surface stays on
        // its own anchor. Close these choices over rigid families before selection, so
        // later area or renderer limits can only remove the complete object.
        foreach (
            var family in Enumerable
                .Range(0, parts.Length)
                .Where(i => membership[i] >= 0 && parts[i].Rig is { Bone: > 0 })
                .GroupBy(i => (Root(i), parts[i].Rig!.Bone))
        )
        {
            var first = membership[family.First()];
            foreach (var id in family)
                groupIds[GroupRoot(membership[id])] = GroupRoot(first);
        }
        return Enumerable
            .Range(0, result.Length)
            .GroupBy(GroupRoot)
            .Select(g => g.SelectMany(i => result[i]).ToArray())
            .ToArray();
    }
}
