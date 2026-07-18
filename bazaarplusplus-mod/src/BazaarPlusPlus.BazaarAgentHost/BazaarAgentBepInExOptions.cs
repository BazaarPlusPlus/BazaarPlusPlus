#nullable enable
using BazaarPlusPlus.BazaarAgent;

namespace BazaarPlusPlus.BazaarAgentHost;

internal sealed class BazaarAgentBepInExOptions : IBazaarAgentOptions
{
    // Anchored on the mod data dir like Core/Paths/BepInExPathProvider. Do not derive from
    // Application.dataPath: on macOS its parent is the .app bundle root, and writing there
    // breaks codesign ("unsealed contents present in the bundle root").
    public string DecisionLogRoot =>
        Path.Combine(BepInEx.Paths.GameRootPath, "BazaarPlusPlusV4", "BazaarAgent");
}
