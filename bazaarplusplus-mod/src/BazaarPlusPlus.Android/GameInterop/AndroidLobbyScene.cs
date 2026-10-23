using Il2Cpp;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// The lobby, as the replay's menu return and History's reopen both read it: the game's
/// <c>SceneID.HeroSelectScene</c>, which the desktop keys on too. The Unity scene that loads is
/// named <c>MainMenuScene</c>, so comparing scene names would never match (panels.md Gotchas);
/// both readers ask the game's <c>SceneLoader</c> instead. Main thread only.
/// </summary>
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

    /// <summary>Loaded and the active scene: the replay has returned and the menu shows.</summary>
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
