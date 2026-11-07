using MelonLoader;

namespace BazaarPlusPlus.Android.Host;

public sealed class AndroidMod : MelonMod
{
    private readonly AndroidHostRuntime _runtime = new();

    public override void OnInitializeMelon() =>
        _runtime.Initialize(HarmonyInstance, LoggerInstance, Info.Version);

    public override void OnUpdate() => _runtime.Frame();

    public override void OnSceneWasLoaded(int buildIndex, string sceneName) =>
        _runtime.SceneLoaded(buildIndex, sceneName);

    public override void OnApplicationQuit() => _runtime.Stop();

    public override void OnDeinitializeMelon() => _runtime.Stop();
}
