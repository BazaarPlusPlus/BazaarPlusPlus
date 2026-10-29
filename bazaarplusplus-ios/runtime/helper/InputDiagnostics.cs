using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace BazaarPlusPlus.Ios.Helper
{
    // BPP_INPUT_DIAG=1: logs the Input System's devices and the first input events, so a game that ignores touches
    // can be diagnosed.
    public static class InputDiagnostics
    {
        static int s_events;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start()
        {
            if (System.Environment.GetEnvironmentVariable("BPP_INPUT_DIAG") != "1") return;
            LogDevices("start");
            InputSystem.onDeviceChange += (d, c) => Debug.Log($"[BppIos] device {c}: {d.layout} {d.name} enabled={d.enabled}");
            InputSystem.onEvent += (ptr, device) =>
            {
                if (s_events++ >= 40) return;
                Debug.Log($"[BppIos] event {ptr.type} on {device?.layout} {device?.name}");
            };
            var settings = InputSystem.settings;
            var sb = new StringBuilder("[BppIos] supportedDevices:");
            foreach (var s in settings.supportedDevices) sb.Append(' ').Append(s);
            sb.Append(" updateMode=").Append(settings.updateMode);
            Debug.Log(sb.ToString());
            Debug.Log($"[BppIos] legacy multiTouch={Input.multiTouchEnabled} simulateMouse={Input.simulateMouseWithTouches}");
        }

        static void LogDevices(string when)
        {
            var sb = new StringBuilder($"[BppIos] devices ({when}):");
            foreach (var d in InputSystem.devices) sb.Append($" {d.layout}/{d.name}(enabled={d.enabled})");
            foreach (var d in InputSystem.disconnectedDevices) sb.Append($" disconnected:{d.layout}");
            Debug.Log(sb.ToString());
        }
    }
}
