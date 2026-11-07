#nullable enable

namespace BazaarPlusPlus.Game.HeroPortraits;

// Rig hints, not skin identifiers. Weighted meshes often attach to a shared root slot;
// their actual influences, rather than that slot, determine the visible layer.
internal sealed class PortraitBoneRoles
{
    private readonly bool[] _foreground;

    internal PortraitBoneRoles(string[] names, int[] parents)
    {
        if (names.Length != parents.Length)
            throw new ArgumentException("Bone names and parent indices must have equal lengths.");
        _foreground = new bool[names.Length];
        var hints = names.Select(Anatomical).ToArray();
        for (var i = 0; i < names.Length; i++)
        {
            var current = i;
            for (
                var depth = 0;
                current >= 0 && current < names.Length && depth < names.Length;
                depth++
            )
            {
                if (hints[current])
                {
                    _foreground[i] = true;
                    break;
                }
                current = parents[current];
            }
        }
    }

    internal bool IsForeground(int slotBone, int[]? bones, float[]? vertices)
    {
        if (bones == null)
            return slotBone >= 0 && slotBone < _foreground.Length && _foreground[slotBone];
        if (vertices == null)
            return false;
        var foreground = 0f;
        var total = 0f;
        var weight = 0;
        for (var i = 0; i < bones.Length; )
        {
            var count = bones[i++];
            if (count < 0 || count > bones.Length - i)
                return false;
            for (var influence = 0; influence < count; influence++)
            {
                var bone = bones[i++];
                if (bone < 0 || bone >= _foreground.Length || weight > vertices.Length - 3)
                    return false;
                var value = vertices[weight + 2];
                weight += 3;
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0)
                    return false;
                total += value;
                if (_foreground[bone])
                    foreground += value;
            }
        }
        return total > 0 && foreground >= total * 0.65f;
    }

    private static bool Anatomical(string name)
    {
        foreach (var raw in name.ToLowerInvariant().Split('_', '-', '.', ' '))
        {
            var token = raw.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            if (
                token
                is "head"
                    or "neck"
                    or "chest"
                    or "spine"
                    or "torso"
                    or "collarbone"
                    or "shoulder"
                    or "arm"
                    or "upperarm"
                    or "lowerarm"
                    or "forearm"
                    or "elbow"
                    or "hand"
                    or "wrist"
                    or "finger"
                    or "thumb"
                    or "hair"
                    or "bangs"
                    or "ponytail"
                    or "face"
            )
                return true;
        }
        return false;
    }
}
