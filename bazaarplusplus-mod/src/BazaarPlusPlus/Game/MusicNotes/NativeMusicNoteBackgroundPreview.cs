#nullable enable
using HarmonyLib;
using UnityEngine;

namespace BazaarPlusPlus.Game.MusicNotes;

// Native placed notes already own a foreground and background. Raise only their hint
// background, using the native entry point; no cloned note and no presenter pulse.
internal sealed class NativeMusicNoteBackgroundPreview
{
    private static readonly System.Reflection.FieldInfo? VfxField = AccessTools.Field(
        typeof(SocketEffectController),
        "_socketVfxController"
    );
    private static readonly System.Reflection.FieldInfo? BackgroundField = AccessTools.Field(
        typeof(SocketEffectVFXController),
        "_hintAboveBoardRoot"
    );
    private readonly Dictionary<GameObject, int> _originalLayers = [];
    private readonly HashSet<GameObject> _seen = [];
    private readonly List<Transform> _transforms = [];
    private readonly List<GameObject> _removed = [];

    internal void BeginRefresh() => _seen.Clear();

    internal void Show(SocketEffectController? controller)
    {
        if (
            controller == null
            || VfxField?.GetValue(controller) is not SocketEffectVFXController vfx
            || vfx == null
            || BackgroundField?.GetValue(vfx) is not GameObject background
            || background == null
        )
            return;

        _transforms.Clear();
        background.GetComponentsInChildren(includeInactive: true, _transforms);
        foreach (var transform in _transforms)
        {
            var target = transform.gameObject;
            _seen.Add(target);
            if (!_originalLayers.ContainsKey(target))
                _originalLayers.Add(target, target.layer);
        }
        vfx.SetHintVisualsAboveBoard(true);
    }

    internal void EndRefresh()
    {
        _removed.Clear();
        foreach (var pair in _originalLayers)
        {
            var target = pair.Key;
            if (_seen.Contains(target))
                continue;
            _removed.Add(target);
            if (target != null)
                target.layer = pair.Value;
        }
        foreach (var target in _removed)
            _originalLayers.Remove(target);
    }

    internal void Restore()
    {
        foreach (var pair in _originalLayers)
            if (pair.Key != null)
                pair.Key.layer = pair.Value;
        _originalLayers.Clear();
        _seen.Clear();
        _removed.Clear();
    }
}
