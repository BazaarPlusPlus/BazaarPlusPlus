#nullable enable
using System.Collections;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;

namespace BazaarPlusPlus.Game.OverlayPanels;

// A Walkthrough Control `shot <name>`: the finished frame of the game window, captured inside the
// game so no screen-recording permission is involved, written to <Data Root>/ui-shots/<name>.png.
// The file appears only when complete (written beside it, then moved), and a log event names it,
// so `just mod::ui-shots` can wait for either.
internal static class DesktopUiShot
{
    internal const string DirectoryName = "ui-shots";

    internal static IEnumerator Capture(string directory, string name)
    {
        yield return new WaitForEndOfFrame();
        Texture2D? texture = null;
        try
        {
            texture = ScreenCapture.CaptureScreenshotAsTexture();
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, name + ".png");
            var partial = path + ".partial";
            File.WriteAllBytes(partial, texture.EncodeToPNG());
            if (File.Exists(path))
                File.Delete(path);
            File.Move(partial, path);
            BppLog.InfoEvent(
                new BppLogEvent(
                    BppLogFeatureScope.OverlayPanels,
                    "overlay_panels.walkthrough.shot"
                ),
                ("name", name),
                ("width", texture.width),
                ("height", texture.height)
            );
        }
        catch (Exception ex)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.OverlayPanels,
                    "overlay_panels.walkthrough.shot_failed"
                ),
                ("name", name),
                ("error", ex.GetType().Name)
            );
        }
        finally
        {
            if (texture != null)
                UnityEngine.Object.Destroy(texture);
        }
    }
}
