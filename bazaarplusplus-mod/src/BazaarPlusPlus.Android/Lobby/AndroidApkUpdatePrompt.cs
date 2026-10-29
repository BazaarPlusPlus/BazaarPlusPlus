using System.Globalization;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.Ui;
using BazaarPlusPlus.Game.Lobby;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Localization;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.Android.Lobby;

/// <summary>
/// The main menu's one popup about a new Android Release APK, in the game's own generic popup.
/// It asks once per published product version when <see cref="AndroidVersionCheck.ApkUpdate"/>
/// names an APK this copy needs; the settings row offers the same download every time. When the
/// game refuses a run because its client is outdated (<see cref="GameRequiresUpdate"/>), the
/// game's own message points at a store that cannot install over a BPP-signed APK, so this asks
/// again once that session: with the download when BPP has published the APK, or with a note that
/// the APK is on its way when it has not.
/// </summary>
internal sealed class AndroidApkUpdatePrompt
{
    // The game opens its own popups as the menu loads; asking waits until they are gone.
    private const float SettleSeconds = 2f;

    private static readonly LocalizedTextSet Title = new(
        "New APK needed",
        "需要新版安装包",
        "需要新版安裝包"
    );
    private static readonly LocalizedTextSet GameUpdatedMessage = new(
        "The game has updated. Download the new BazaarPlusPlus APK ({0}). Installing it over this one keeps your data.",
        "游戏已更新。请下载新版 BazaarPlusPlus 安装包（{0}），覆盖安装后数据会保留。",
        "遊戲已更新。請下載新版 BazaarPlusPlus 安裝包（{0}），覆蓋安裝後資料會保留。"
    );
    private static readonly LocalizedTextSet RuntimeChangedMessage = new(
        "BazaarPlusPlus {1} cannot update inside the game. Download the new APK ({0}). Installing it over this one keeps your data.",
        "BazaarPlusPlus {1} 不能在游戏内更新。请下载新版安装包（{0}），覆盖安装后数据会保留。",
        "BazaarPlusPlus {1} 不能在遊戲內更新。請下載新版安裝包（{0}），覆蓋安裝後資料會保留。"
    );
    private static readonly LocalizedTextSet PendingTitle = new(
        "The game has updated",
        "游戏已更新",
        "遊戲已更新"
    );
    private static readonly LocalizedTextSet PendingMessage = new(
        "BazaarPlusPlus is being updated for the new game. When its APK is out, this menu offers the download.",
        "BazaarPlusPlus 正在适配新版游戏。新版安装包发布后，会在这里提示下载。",
        "BazaarPlusPlus 正在適配新版遊戲。新版安裝包發布後，會在這裡提示下載。"
    );
    private static readonly LocalizedTextSet Download = new("Download", "下载", "下載");
    private static readonly LocalizedTextSet Later = new("Later", "稍后", "稍後");
    private static readonly LocalizedTextSet Ok = new("OK", "好", "好");

    private readonly AndroidVersionCheck _check;
    private readonly string _recordPath;
    private readonly Action<AndroidApkUpdate> _download;
    private readonly Func<bool> _panelOpen;
    private float? _menuSince;
    private volatile bool _gameRequiresUpdate;
    private bool _askedForGame;

    /// <param name="check">The main menu's release check.</param>
    /// <param name="dataRoot">The Data Root, where the last product version asked about is kept.</param>
    /// <param name="download">Starts the download of the APK this copy needs.</param>
    /// <param name="panelOpen">Whether a BPP panel covers the menu.</param>
    public AndroidApkUpdatePrompt(
        AndroidVersionCheck check,
        string dataRoot,
        Action<AndroidApkUpdate> download,
        Func<bool> panelOpen
    )
    {
        _check = check ?? throw new ArgumentNullException(nameof(check));
        _recordPath = Path.Combine(dataRoot, "android-apk-prompt.txt");
        _download = download ?? throw new ArgumentNullException(nameof(download));
        _panelOpen = panelOpen ?? throw new ArgumentNullException(nameof(panelOpen));
    }

    /// <summary>The game refused a run because its client is outdated; any thread.</summary>
    public void GameRequiresUpdate() => _gameRequiresUpdate = true;

    /// <summary>Main thread, every frame.</summary>
    public void Tick()
    {
        // Only the main menu asks; the loading scene the game adds on top leaves it active.
        if (!AndroidLobbyScene.IsActive())
        {
            _menuSince = null;
            return;
        }
        _menuSince ??= Time.unscaledTime;
        if (
            Time.unscaledTime - _menuSince.Value < SettleSeconds
            || AndroidNativeHud.Popups is not { } popups
            || AndroidNativeHud.HasPopup
            || _panelOpen()
        )
            return;

        var update = _check.ApkUpdate;
        var version = _check.LatestVersion;
        if (_gameRequiresUpdate && !_askedForGame)
        {
            var status = _check.GameUpdateStatus;
            if (status == AndroidGameUpdateStatus.Checking)
                return;
            _askedForGame = true;
            if (update != null)
                Ask(popups, update, version, "game_requires_update");
            else
                Tell(popups);
            return;
        }
        if (update == null || version == null || version == ReadAsked())
            return;
        WriteAsked(version);
        Ask(popups, update, version, "release_checked");
    }

    private void Ask(PopupManager popups, AndroidApkUpdate update, string? version, string source)
    {
        var size = FormatSize(update.Apk.Size);
        var message =
            update.Reason == AndroidApkUpdateReason.GameUpdated
                ? string.Format(CultureInfo.InvariantCulture, L.Resolve(GameUpdatedMessage), size)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    L.Resolve(RuntimeChangedMessage),
                    size,
                    version
                );
        popups.ShowGenericPopup(
            L.Resolve(Title),
            message,
            Buttons(
                (
                    L.Resolve(Download),
                    () =>
                    {
                        Log(
                            new BppLogEvent(BppLogFeatureScope.Lobby, "lobby.apk_prompt.download"),
                            update,
                            source
                        );
                        _download(update);
                    }
                ),
                (
                    L.Resolve(Later),
                    () =>
                        Log(
                            new BppLogEvent(BppLogFeatureScope.Lobby, "lobby.apk_prompt.later"),
                            update,
                            source
                        )
                )
            )
        );
        Log(new BppLogEvent(BppLogFeatureScope.Lobby, "lobby.apk_prompt.shown"), update, source);
    }

    private static void Tell(PopupManager popups)
    {
        popups.ShowGenericPopup(
            L.Resolve(PendingTitle),
            L.Resolve(PendingMessage),
            Buttons((L.Resolve(Ok), () => { }))
        );
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Lobby, "lobby.apk_prompt.pending_shown")
        );
    }

    private static Il2CppReferenceArray<GenericPopupButton> Buttons(
        params (string Text, Action OnClick)[] buttons
    )
    {
        var array = new Il2CppReferenceArray<GenericPopupButton>((long)buttons.Length);
        for (var i = 0; i < buttons.Length; i++)
        {
            var onClick = buttons[i].OnClick;
            array[i] = new GenericPopupButton(
                buttons[i].Text,
                DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(() =>
                    AndroidUiActions.Run("apk_prompt", onClick)
                )
            );
        }
        return array;
    }

    private static void Log(BppLogEvent logEvent, AndroidApkUpdate update, string source) =>
        BppLog.InfoEvent(
            logEvent,
            ("reason", update.Reason),
            ("source", source),
            ("game_version", update.Apk.GameVersion)
        );

    private static string FormatSize(long bytes) =>
        (bytes / (1024d * 1024 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " GB";

    private string? ReadAsked()
    {
        try
        {
            return File.Exists(_recordPath) ? File.ReadAllText(_recordPath).Trim() : null;
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(BppLogFeatureScope.Lobby, "lobby.apk_prompt.record_failed"),
                exception,
                ("operation", "read")
            );
            return null;
        }
    }

    private void WriteAsked(string version)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_recordPath)!);
            File.WriteAllText(_recordPath, version);
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(BppLogFeatureScope.Lobby, "lobby.apk_prompt.record_failed"),
                exception,
                ("operation", "write")
            );
        }
    }
}
