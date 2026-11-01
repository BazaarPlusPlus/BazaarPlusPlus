using System.Globalization;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.Host;
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
/// The main menu's one popup about an update the mod cannot install, in the game's own generic
/// popup (root ADR 0012). It asks once per published product version when
/// <see cref="AndroidVersionCheck.Update"/> names one: a newer game, which the user updates from
/// its store and then starts again from the Android App, with no BPP download; or a new Android
/// App, with its download. The settings row says the same every time. When the game refuses a
/// run because its client is outdated (<see cref="GameRequiresUpdate"/>), the game's own message
/// sends the user to the store but not back to the Android App, so this tells them once that
/// session.
/// </summary>
internal sealed class AndroidUpdatePrompt
{
    // The game opens its own popups as the menu loads; asking waits until they are gone.
    private const float SettleSeconds = 2f;

    private static readonly LocalizedTextSet GameTitle = new(
        "Update The Bazaar",
        "请更新 The Bazaar",
        "請更新 The Bazaar"
    );
    private static readonly LocalizedTextSet GameMessage = new(
        "The Bazaar has a newer version. Update it from Google Play or the publisher's site, then start it again from the BazaarPlusPlus App.",
        "The Bazaar 有新版本。请从 Google Play 或发行商的网站更新，然后从 BazaarPlusPlus App 重新启动游戏。",
        "The Bazaar 有新版本。請從 Google Play 或發行商的網站更新，然後從 BazaarPlusPlus App 重新啟動遊戲。"
    );
    private static readonly LocalizedTextSet AppTitle = new(
        "Install the new BazaarPlusPlus App",
        "请安装新版 BazaarPlusPlus App",
        "請安裝新版 BazaarPlusPlus App"
    );
    private static readonly LocalizedTextSet AppMessage = new(
        "BazaarPlusPlus {1} needs a new BazaarPlusPlus App. Download it ({0}), install it, then start the game from it.",
        "BazaarPlusPlus {1} 需要新版 BazaarPlusPlus App。请下载（{0}）并安装，然后从它启动游戏。",
        "BazaarPlusPlus {1} 需要新版 BazaarPlusPlus App。請下載（{0}）並安裝，然後從它啟動遊戲。"
    );
    private static readonly LocalizedTextSet Download = new("Download", "下载", "下載");
    private static readonly LocalizedTextSet Later = new("Later", "稍后", "稍後");
    private static readonly LocalizedTextSet Ok = new("OK", "好", "好");

    private readonly AndroidVersionCheck _check;
    private readonly string _recordPath;
    private readonly Action<AndroidUpdate> _download;
    private readonly Func<bool> _panelOpen;
    private float? _menuSince;
    private volatile bool _gameRequiresUpdate;
    private bool _toldForGame;

    /// <param name="check">The main menu's release check.</param>
    /// <param name="dataRoot">The Data Root, where the last product version asked about is kept.</param>
    /// <param name="download">Opens the download of the new Android App.</param>
    /// <param name="panelOpen">Whether a BPP panel covers the menu.</param>
    public AndroidUpdatePrompt(
        AndroidVersionCheck check,
        string dataRoot,
        Action<AndroidUpdate> download,
        Func<bool> panelOpen
    )
    {
        _check = check ?? throw new ArgumentNullException(nameof(check));
        _recordPath = Path.Combine(dataRoot, "android-update-prompt.txt");
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

        if (_gameRequiresUpdate && !_toldForGame)
        {
            _toldForGame = true;
            TellGameUpdate(popups, "game_requires_update", _check.Update?.PublishedGameVersion);
            return;
        }
        var update = _check.Update;
        var version = _check.LatestVersion;
        if (update == null || version == null || version == ReadAsked())
            return;
        WriteAsked(version);
        if (update.Reason == AndroidUpdateReason.GameUpdated)
        {
            _toldForGame = true;
            TellGameUpdate(popups, "release_checked", update.PublishedGameVersion);
        }
        else if (update.App is { } app)
            OfferApp(popups, update, app, version);
    }

    private static void TellGameUpdate(
        PopupManager popups,
        string source,
        string? publishedGameVersion
    )
    {
        popups.ShowGenericPopup(
            L.Resolve(GameTitle),
            L.Resolve(GameMessage),
            Buttons((L.Resolve(Ok), () => { }))
        );
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Lobby, "lobby.update_prompt.game_shown"),
            ("source", source),
            ("published_game_version", publishedGameVersion)
        );
    }

    private void OfferApp(
        PopupManager popups,
        AndroidUpdate update,
        AndroidAppDownload app,
        string version
    )
    {
        var message = string.Format(
            CultureInfo.InvariantCulture,
            L.Resolve(AppMessage),
            FormatSize(app.Size),
            version
        );
        popups.ShowGenericPopup(
            L.Resolve(AppTitle),
            message,
            Buttons(
                (
                    L.Resolve(Download),
                    () =>
                    {
                        LogApp(
                            new BppLogEvent(
                                BppLogFeatureScope.Lobby,
                                "lobby.update_prompt.app_download"
                            ),
                            version
                        );
                        _download(update);
                    }
                ),
                (
                    L.Resolve(Later),
                    () =>
                        LogApp(
                            new BppLogEvent(
                                BppLogFeatureScope.Lobby,
                                "lobby.update_prompt.app_later"
                            ),
                            version
                        )
                )
            )
        );
        LogApp(new BppLogEvent(BppLogFeatureScope.Lobby, "lobby.update_prompt.app_shown"), version);
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
                    AndroidUiActions.Run("update_prompt", onClick)
                )
            );
        }
        return array;
    }

    private static void LogApp(BppLogEvent logEvent, string version) =>
        BppLog.InfoEvent(logEvent, ("source", "release_checked"), ("version", version));

    private static string FormatSize(long bytes) =>
        bytes >= 1024L * 1024 * 1024
            ? (bytes / (1024d * 1024 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " GB"
            : (bytes / (1024d * 1024)).ToString("0", CultureInfo.InvariantCulture) + " MB";

    private string? ReadAsked()
    {
        try
        {
            return File.Exists(_recordPath) ? File.ReadAllText(_recordPath).Trim() : null;
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(BppLogFeatureScope.Lobby, "lobby.update_prompt.record_failed"),
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
                new BppLogEvent(BppLogFeatureScope.Lobby, "lobby.update_prompt.record_failed"),
                exception,
                ("operation", "write")
            );
        }
    }
}
