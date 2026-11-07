#nullable enable
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.HeroPortraits;

// Record the game's actual sprite/skin pair, including opponents' unowned cosmetics.
// Weak keys and controller references must not keep a departed scene's assets alive.
internal sealed class NativeHeroPortraitCatalog
{
    private readonly ConditionalWeakTable<Sprite, NativeHeroPortraitSkin> _skins = new();
    private readonly List<WeakReference<EncounterController>> _encounters = new();

    internal void Record(NativeHeroPortraitSkin skin, Sprite? sprite)
    {
        if (sprite == null)
            return;
        _skins.Remove(sprite);
        _skins.Add(sprite, skin);
    }

    internal void Observe(EncounterController encounter)
    {
        if (encounter == null)
            return;
        foreach (var reference in _encounters)
            if (reference.TryGetTarget(out var existing) && existing == encounter)
                return;
        _encounters.Add(new WeakReference<EncounterController>(encounter));
    }

    internal bool TryResolve(Sprite sprite, out NativeHeroPortraitSkin skin, out string reason)
    {
        if (!_skins.TryGetValue(sprite, out skin!))
            reason = "skin_not_recorded";
        else if (skin.NativeAnimated)
            reason = "native_animated_skin";
        else if (skin.CharacterPrefab == null || !skin.CharacterPrefab.RuntimeKeyIsValid())
            reason = "missing_character_prefab";
        else
        {
            reason = "eligible";
            return true;
        }
        return false;
    }

    internal void CopyPortraits(List<SpriteRenderer> portraits)
    {
        portraits.Clear();
        for (var i = _encounters.Count - 1; i >= 0; i--)
        {
            if (!_encounters[i].TryGetTarget(out var encounter) || encounter == null)
            {
                _encounters.RemoveAt(i);
                continue;
            }
            if (
                encounter.Portrait != null
                && encounter.Portrait.TryGetComponent<SpriteRenderer>(out var renderer)
            )
                portraits.Add(renderer);
        }
    }
}
