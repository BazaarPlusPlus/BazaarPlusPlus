using BazaarPlusPlus.Android.GameInterop;
using HarmonyLib;
using Il2CppManagers;
using Il2CppManagers.Login;
using UnityEngine;

namespace BazaarPlusPlus.Android.Host;

// The debug App supplies this mailbox before CoreCLR starts. The release App never does.
// Only this main-thread adapter touches the game's login flow; the game owns authentication.
internal sealed class AndroidDevLogin(string directory, HarmonyLib.Harmony harmony)
{
    private static AndroidDevLogin? _active;
    private readonly DevLoginMailbox _mailbox = new(directory);
    private DevLoginMailbox.Request? _request;
    private long _nextPoll;
    private bool _submitted;
    private bool _failureNotice;
    private bool _finished;
    private string _phase = "starting";
    private LoginManager? _manager;

    public static AndroidDevLogin? Create(HarmonyLib.Harmony harmony)
    {
        var directory = Environment.GetEnvironmentVariable("BPP_DEV_LOGIN_DIR");
        return string.IsNullOrEmpty(directory) ? null : new(directory, harmony);
    }

    public void Tick()
    {
        if (_finished || Environment.TickCount64 < _nextPoll)
            return;
        _nextPoll = Environment.TickCount64 + 500;
        try
        {
            _request ??= _mailbox.Take();
            if (_request == null)
                return;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (_request.ExpiresAt > now + 600_000)
            {
                Finish("invalid_request");
                return;
            }
            if (now >= _request.ExpiresAt)
            {
                Finish("timeout");
                return;
            }
            if (_manager == null)
                _manager = UnityEngine
                    .Object.FindObjectsByType<LoginManager>(FindObjectsSortMode.None)
                    .FirstOrDefault();
            var machine = _manager?.StateMachine;
            if (machine?._currentState == null)
                return;
            var state = machine.CurrentState;
            _phase = state.ToString();
            var login =
                state == ELoginState.LogIn ? machine._currentState.TryCast<LoginState>() : null;
            var outcome = DevLoginOutcome.Read(
                _phase,
                state == ELoginState.PlayNowValidated
                    && AndroidClientCache.TryGetProfileAccountId() != null,
                _submitted,
                login?._submitting ?? true,
                _failureNotice
            );
            if (outcome != null)
            {
                Finish(outcome);
                return;
            }
            if (_request.Mode == "manual" || _submitted)
                return;
            if (state is not (ELoginState.MobileLanding or ELoginState.LogIn))
                return;
            if (_request.Mode == "resume")
            {
                Finish("needs_credentials");
                return;
            }
            if (state == ELoginState.MobileLanding)
            {
                machine.GoToNextState(ELoginState.LogIn);
                return;
            }
            var view = UnityEngine
                .Object.FindObjectsByType<LoginStateView>(FindObjectsSortMode.None)
                .FirstOrDefault();
            // OnEnter binds the Continue handler; seeing the component alone is insufficient.
            if (view?.OnContinue == null || view.emailText == null || view.passwordText == null)
                return;
            _active = this;
            harmony.Patch(
                AccessTools.Method(typeof(LoginManager), nameof(LoginManager.ShowNotice)),
                postfix: new HarmonyMethod(typeof(AndroidDevLogin), nameof(OnNotice))
            );
            view.emailText.text = _request.Email;
            view.passwordText.text = _request.Password;
            _request.ForgetCredentials();
            _submitted = true;
            view.OnContinueClicked();
        }
        catch
        {
            // Exception bodies from JSON, Unity or authentication may contain credentials.
            Finish("adapter_failed");
        }
    }

    // Observe only the game's explicit failure signal, never its message (which can hold PII).
    private static void OnNotice(LoginManager __instance, string __0, bool __1)
    {
        if (
            !__1
            && !string.IsNullOrWhiteSpace(__0)
            && _active is { _submitted: true, _finished: false } active
            && __instance == active._manager
        )
            active._failureNotice = true;
    }

    private void Finish(string status)
    {
        _finished = true;
        _active = null;
        if (_request == null)
            return;
        try
        {
            _mailbox.Report(
                _request,
                Environment.ProcessId,
                Application.version,
                status,
                _phase,
                _submitted
            );
        }
        catch
        {
            // The CLI stops an unacknowledged process and removes the request on its deadline.
        }
    }
}
