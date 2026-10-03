#nullable enable

namespace BazaarPlusPlus;

internal enum PluginInitializationPhase
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

internal enum PluginLogReasonCode
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
