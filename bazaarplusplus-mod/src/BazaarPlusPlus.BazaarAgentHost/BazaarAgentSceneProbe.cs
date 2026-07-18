#nullable enable
using System.Reflection;
using BazaarPlusPlus.BazaarAgent;
using HarmonyLib;
using TheBazaar;
using UnityEngine.SceneManagement;

namespace BazaarPlusPlus.BazaarAgentHost;

/// <summary>Determines whether the game is sitting on the hero-select lobby
/// with no active session, i.e. it is safe to invoke
/// <c>GameInstance.Instance.StartNewRun()</c> via the dispatcher. All checks
/// are conservative — a false negative just means the action isn't offered
/// this tick, while a false positive would let StartNewRun fire when it
/// would be rejected client- or server-side.</summary>
internal static class BazaarAgentSceneProbe
{
    private const string HeroSelectSceneName = "HeroSelectScene";

    private static FieldInfo? _clientCacheProfileField;
    private static PropertyInfo? _profileValueProp;
    private static bool _reflectionAttempted;
    private static bool _permanentDegradationReported;

    private static (bool sceneOk, bool appStateNull, bool profileLoaded)? _lastDiagnosis;
    private static readonly BazaarAgentDegradationLogState TransientLogState = new();

    public static bool IsAtHeroSelectAndReadyForNewRun(IBazaarAgentLogger logger)
    {
        try
        {
            var sceneName = SceneManager.GetActiveScene().name;
            var sceneOk = string.Equals(sceneName, HeroSelectSceneName, StringComparison.Ordinal);
            var appStateNull = AppState.CurrentState == null;
            var profileResult = TryReadProfileLoaded();
            var profileLoaded = profileResult.Loaded;
            if (profileResult.PermanentFailure is { } permanentFailure)
            {
                if (!_permanentDegradationReported)
                {
                    _permanentDegradationReported = true;
                    logger.TryEmit(BazaarAgentLogEvents.SceneProbeDegraded(permanentFailure));
                }
            }
            else
            {
                TransientLogState.ReportRecovered(logger, BazaarAgentLogEvents.SceneProbeRecovered);
            }

            var snapshot = (sceneOk, appStateNull, profileLoaded);
            if (_lastDiagnosis != snapshot)
            {
                _lastDiagnosis = snapshot;
                logger.TryEmitDebug(() =>
                    BazaarAgentLogEvents.SceneProbeStateChanged(
                        sceneName,
                        sceneOk,
                        appStateNull,
                        profileLoaded
                    )
                );
            }

            return sceneOk && appStateNull && profileLoaded;
        }
        catch (Exception ex)
        {
            TransientLogState.ReportDegraded(
                logger,
                () =>
                    BazaarAgentLogEvents.SceneProbeDegraded(
                        BazaarAgentLogReasonCode.SceneProbeException,
                        ex
                    )
            );
            return false;
        }
    }

    private static ProfileReadResult TryReadProfileLoaded()
    {
        if (!_reflectionAttempted)
        {
            _reflectionAttempted = true;
            var clientCacheType = AccessTools.TypeByName("TheBazaar.ClientCache");
            if (clientCacheType is null)
            {
                return ProfileReadResult.Permanent(
                    BazaarAgentLogReasonCode.ClientCacheTypeUnavailable
                );
            }
            _clientCacheProfileField = clientCacheType.GetField(
                "Profile",
                BindingFlags.Static | BindingFlags.Public
            );
            if (_clientCacheProfileField is not null)
            {
                _profileValueProp = _clientCacheProfileField.FieldType.GetProperty("Value");
            }
        }
        if (_clientCacheProfileField is null)
            return ProfileReadResult.Permanent(BazaarAgentLogReasonCode.ProfileFieldUnavailable);
        if (_profileValueProp is null)
            return ProfileReadResult.Permanent(
                BazaarAgentLogReasonCode.ProfileValuePropertyUnavailable
            );
        var profileCache = _clientCacheProfileField.GetValue(null);
        if (profileCache is null)
            return ProfileReadResult.Healthy(loaded: false);
        var profile = _profileValueProp.GetValue(profileCache);
        return ProfileReadResult.Healthy(profile is not null);
    }

    private readonly struct ProfileReadResult
    {
        private ProfileReadResult(bool loaded, BazaarAgentLogReasonCode? permanentFailure)
        {
            Loaded = loaded;
            PermanentFailure = permanentFailure;
        }

        internal bool Loaded { get; }
        internal BazaarAgentLogReasonCode? PermanentFailure { get; }

        internal static ProfileReadResult Healthy(bool loaded) => new(loaded, null);

        internal static ProfileReadResult Permanent(BazaarAgentLogReasonCode reasonCode) =>
            new(loaded: false, reasonCode);
    }
}
