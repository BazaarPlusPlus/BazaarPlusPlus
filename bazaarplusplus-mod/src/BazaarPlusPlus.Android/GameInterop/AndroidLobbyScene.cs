using Il2Cpp;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.GameInterop;

// The Unity scene is named MainMenuScene even though the game's SceneLoader calls it
// HeroSelectScene.
internal static class AndroidLobbyScene
{
    public const SceneID Lobby = SceneID.HeroSelectScene;

    public static bool IsLoaded()
    {
        try
        {
            return SceneLoader.IsSceneLoaded(Lobby);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsActive()
    {
        try
        {
            return SceneLoader.IsSceneLoaded(Lobby) && SceneLoader.ActiveScene == Lobby;
        }
        catch
        {
            return false;
        }
    }

    public static string Describe()
    {
        try
        {
            return $"{SceneLoader.ActiveScene}:{(SceneLoader.IsSceneLoaded(Lobby) ? "lobby_loaded" : "lobby_unloaded")}";
        }
        catch (Exception exception)
        {
            return $"unavailable:{exception.GetType().Name}";
        }
    }
}
