using BazaarPlusPlus.Android.Patches;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// The Android back key (Escape under Unity) closes the top-most BPP panel or dialog before the
/// game sees it: otherwise the game's <c>PopupManager.HandleEscape</c> opens its escape menu over
/// the open BPP panel. The handlers are asked top-most first; the first that closes something
/// consumes the press. One press can reach BPP twice in a frame, through the game's escape hook
/// (<see cref="BackKeyPatches"/>) and through the touch root's own keyboard read, so a consumed
/// press is stamped with its frame and the second sighting is consumed without closing a second
/// panel. Main thread only.
/// </summary>
internal static class AndroidBackKey
{
    private const long LoggedPasses = 5;

    private static readonly List<(string Name, Func<bool> TryClose)> Handlers = new();
    private static int _consumedFrame = -1;
    private static int _seenFrame = -1;
    private static long _presses;
    private static long _consumed;
    private static long _passed;

    /// <summary>
    /// Adds a handler below every handler registered before it. A name registered again, as when
    /// the touch UI mounts again over a rebuilt canvas, replaces its handler in place, so one
    /// press never asks the same panel twice.
    /// </summary>
    public static void Register(string name, Func<bool> tryClose)
    {
        ArgumentNullException.ThrowIfNull(tryClose);
        var index = Handlers.FindIndex(handler =>
            string.Equals(handler.Name, name, StringComparison.Ordinal)
        );
        if (index >= 0)
            Handlers[index] = (name, tryClose);
        else
            Handlers.Add((name, tryClose));
    }

    /// <summary>The registered handler names, top-most first.</summary>
    public static IReadOnlyList<string> Registered =>
        Handlers.Select(handler => handler.Name).ToList();

    /// <summary>
    /// A back press reached BPP through <paramref name="source"/>; true when BPP closed something
    /// for it, so the game must not handle it.
    /// </summary>
    public static bool TryConsume(string source)
    {
        var frame = Time.frameCount;
        if (frame == _consumedFrame)
            return true;
        var firstSighting = frame != _seenFrame;
        _seenFrame = frame;
        if (firstSighting)
            _presses++;
        foreach (var (name, tryClose) in Handlers)
        {
            bool closed;
            try
            {
                closed = tryClose();
            }
            catch (Exception exception)
            {
                AndroidPatchHost.ReportProbeError($"back_key:{name}", exception);
                continue;
            }
            if (!closed)
                continue;
            _consumedFrame = frame;
            _consumed++;
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.back_key.consumed"),
                ("source", source),
                ("target", name)
            );
            return true;
        }
        if (firstSighting && ++_passed <= LoggedPasses)
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.back_key.passed"),
                ("source", source)
            );
        return false;
    }

    public static BppLogField[] Summary() =>
        [
            ("back_key_presses", _presses),
            ("back_key_consumed", _consumed),
            ("back_key_passed", _passed),
            ("back_key_handlers", Handlers.Count),
        ];
}
