#nullable enable

namespace BazaarPlusPlus;

public enum PluginInitializationPhase
{
    PluginVersion,
    Composition,
    StaticUtilities,
    HarmonyPatches,
    ReplayRuntime,
    Features,
    OnlineServices,
    Mountables,
}

public enum PluginLogReasonCode
{
    VersionUnreadable,
    DetectionSignalsDisagree,
    InitializationException,
    TeardownStepFailed,
    PatchClassException,
    PatchClassesFailed,
    HandlerException,
    FeatureException,
}
