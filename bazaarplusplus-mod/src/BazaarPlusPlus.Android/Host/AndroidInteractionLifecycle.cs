namespace BazaarPlusPlus.Android.Host;

internal sealed class AndroidInteractionLifecycle
{
    private readonly AndroidInteractionOperations _operations;
    private bool _stopped;

    public AndroidInteractionLifecycle(AndroidInteractionOperations operations) =>
        _operations = operations;

    public void Start(Action construct, Action rollback, Action<Exception> report)
    {
        try
        {
            construct();
        }
        catch (Exception exception)
        {
            rollback();
            report(exception);
        }
    }

    public void StartHistory(Action construct, Action rollback, Action<Exception> report) =>
        Start(construct, rollback, report);

    public void Frame()
    {
        _operations.BindMainThread();
        _operations.Pump();
        _operations.DevLogin();
        _operations.Record();
        _operations.Replay();
        _operations.Panels();
        _operations.MountTouch();
        _operations.Guarded("touch_ui", _operations.Touch);
        _operations.Guarded("quest_preview", _operations.Quest);
        _operations.Guarded("encounter_preview", _operations.Encounter);
        _operations.Font();
        _operations.Settings();
        _operations.Rental();
        _operations.Version();
        _operations.Guarded("update_prompt", _operations.Prompt);
        _operations.RefreshLabel();
    }

    public void SceneLoaded(bool mainMenu)
    {
        _operations.Guarded("scene_loaded:upgrade_preview", _operations.ReleaseUpgrade);
        _operations.Guarded("scene_loaded:combat_status", _operations.ResetCombat);
        _operations.Guarded("scene_loaded:overlay_panels", _operations.ScenePanels);
        if (mainMenu)
            _operations.Guarded("scene_loaded:version_check", _operations.BeginVersion);
    }

    public void PauseChanged(bool paused) =>
        _operations.Guarded(
            paused ? "app_lifecycle:pause" : "app_lifecycle:resume",
            () =>
            {
                if (paused)
                {
                    _operations.PauseTouch();
                    _operations.PauseRecord();
                }
                else
                    _operations.ResumeRecord();
            }
        );

    public void Stop()
    {
        if (_stopped)
            return;
        _operations.RetireFeatures();
        _operations.StopTouch();
        _operations.StopHistory();
        _operations.StopReplay();
        _operations.StopPanels();
        _operations.StopFont();
        _operations.StopPump();
        _operations.StopRecord();
        _stopped = true;
    }
}

internal sealed record AndroidInteractionOperations
{
    public required Action BindMainThread { get; init; }
    public required Action Pump { get; init; }
    public required Action DevLogin { get; init; }
    public required Action Record { get; init; }
    public required Action Replay { get; init; }
    public required Action Panels { get; init; }
    public required Action MountTouch { get; init; }
    public required Action Touch { get; init; }
    public required Action Quest { get; init; }
    public required Action Encounter { get; init; }
    public required Action Font { get; init; }
    public required Action Settings { get; init; }
    public required Action Rental { get; init; }
    public required Action Version { get; init; }
    public required Action Prompt { get; init; }
    public required Action RefreshLabel { get; init; }
    public required Action ReleaseUpgrade { get; init; }
    public required Action ResetCombat { get; init; }
    public required Action ScenePanels { get; init; }
    public required Action BeginVersion { get; init; }
    public required Action PauseTouch { get; init; }
    public required Action PauseRecord { get; init; }
    public required Action ResumeRecord { get; init; }
    public required Action RetireFeatures { get; init; }
    public required Action StopTouch { get; init; }
    public required Action StopHistory { get; init; }
    public required Action StopReplay { get; init; }
    public required Action StopPanels { get; init; }
    public required Action StopFont { get; init; }
    public required Action StopPump { get; init; }
    public required Action StopRecord { get; init; }
    public required Action<string, Action> Guarded { get; init; }
}
