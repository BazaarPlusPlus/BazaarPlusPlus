#nullable enable
using UnityEngine.SceneManagement;

namespace BazaarPlusPlus.GameInterop.Scenes;

// Scene.name marshals a new managed string on each read. The name is fixed for a scene handle.
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
