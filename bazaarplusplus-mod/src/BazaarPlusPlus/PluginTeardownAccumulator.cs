#nullable enable
namespace BazaarPlusPlus;

internal enum PluginTeardownStep
{
    None,
    UnpatchHarmony,
    UnmountComponents,
    DestroyCombatReplayRuntime,
    DisposeComposition,
    DisposeOnlineServices,
    UninstallStaticUtilities,
    ResetPatchHost,
}

internal sealed class PluginTeardownAccumulator
{
    internal int FailedStepCount { get; private set; }
    internal PluginTeardownStep FirstFailedStep { get; private set; }
    internal Exception? FirstException { get; private set; }

    internal void Run(PluginTeardownStep step, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            FailedStepCount++;
            if (FirstException != null)
                return;

            FirstFailedStep = step;
            FirstException = ex;
        }
    }
}
