using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace BazaarPlusPlus.Ios.Helper
{
    // Every 5 seconds logs the frame rate and the worst frame time, and logs each frame over 100ms; on each touch press logs the position and the
    // UI objects under it. Enabled by BPP_FRAME_STATS=1.
    public sealed class FrameStats : MonoBehaviour
    {
        float _windowStart;
        int _frames;
        float _worst;
        bool _wasPressed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start()
        {
            if (Environment.GetEnvironmentVariable("BPP_FRAME_STATS") != "1") return;
            var go = new GameObject("BppIos.FrameStats");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<FrameStats>();
        }

        void Update()
        {
            _frames++;
            _worst = Mathf.Max(_worst, Time.unscaledDeltaTime);
            // Same clock as the shim's BPP_GC_STATS lines (mach_absolute_time), so hitches line up with collections.
            if (Time.unscaledDeltaTime > 0.1f)
                Debug.Log($"[BppIos] hitch {Time.unscaledDeltaTime * 1000:F0}ms t={System.Diagnostics.Stopwatch.GetTimestamp() / 1e7:F3}");
            float now = Time.realtimeSinceStartup;
            if (now - _windowStart >= 5f)
            {
                Debug.Log($"[BppIos] fps={_frames / (now - _windowStart):F1} worst={_worst * 1000:F0}ms timeScale={Time.timeScale}");
                _windowStart = now; _frames = 0; _worst = 0;
            }
            var touch = Touchscreen.current?.primaryTouch;
            bool pressed = touch != null && touch.press.isPressed;
            if (pressed && !_wasPressed) LogPress(touch.position.ReadValue());
            _wasPressed = pressed;
        }

        static void LogPress(Vector2 position)
        {
            var hits = new List<RaycastResult>();
            var events = EventSystem.current;
            if (events != null) events.RaycastAll(new PointerEventData(events) { position = position }, hits);
            var names = new List<string>();
            for (int i = 0; i < hits.Count && i < 4; i++) names.Add(hits[i].gameObject.name);
            Debug.Log($"[BppIos] press at {position} screen={Screen.width}x{Screen.height} " +
                      $"eventSystem={(events ? events.name : "none")} module={(events ? events.currentInputModule?.GetType().Name : "")} hits=[{string.Join(", ", names)}]");
        }
    }
}
