#nullable enable
using UnityEngine.SceneManagement;

namespace BazaarPlusPlus.GameInterop.Scenes;

/// <summary>
/// Reads the active scene's name for per-frame callers. <c>Scene.name</c> marshals a fresh managed
/// string out of the native runtime on every read, and the name is fixed for a given scene handle,
/// so the marshalled string is cached until the active scene (or its loaded flag) changes.
/// </summary>
internal static class ActiveSceneNameCache
{
    private static Scene _scene;
    private static bool _loaded;
    private static bool _hasName;
    private static string _name = string.Empty;

    internal static string Current
    {
        get
        {
            var scene = SceneManager.GetActiveScene();
            if (_hasName && _scene == scene && _loaded == scene.isLoaded)
                return _name;

            _scene = scene;
            _loaded = scene.isLoaded;
            _name = scene.name;
            _hasName = true;
            return _name;
        }
    }
}
