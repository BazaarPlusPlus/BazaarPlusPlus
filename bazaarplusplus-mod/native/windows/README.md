# Windows native replay recorder

`GfxPluginBppReplayMediaFoundation.dll` is a Unity D3D11 rendering plugin. It keeps the real-time
path inside the Unity process and does not use an external encoder process. macOS uses the matching
Metal/VideoToolbox/AVFoundation native backend in `native/macos`.

## Data path

1. `CommandBuffer.IssuePluginEventAndData` places a native callback after Unity's final render
   commands.
2. The callback converts the persistent replay capture texture to NV12 with the D3D11 video
   processor.
3. Media Foundation encodes H.264 and the sink writer produces the silent first-pass MP4. The active
   encoder is checked against Media Foundation's hardware MFT catalog; software encoding is rejected
   instead of being used as a hidden fallback.
4. The existing WASAPI loopback capture closes its PCM WAV at replay end.
5. `BppMfMuxAudio` copies the H.264 samples and uses Media Foundation to mix/resample the WAV input
   to stereo 48 kHz AAC in the final MP4.

## Build

Build on Windows with Visual Studio 2022 and the Windows SDK:

```powershell
./build.ps1
```

The script builds into `build/` by default, verifies the x64 PE metadata, exact catalog ABI exports,
reviewed system dependencies, and unsigned producer policy, then runs the native smoke program.
Pass `-OutputDirectory` for a side-effect-free staging build. `test.ps1` runs the smoke program on
its own.

`./run.sh publish` reuses the installer's staged copy only when both the Windows input digest and the
staged artifacts still match the manifest, and otherwise rebuilds through this script and promotes
the result. The freshness contract and the full promotion sequence are in
[`docs/architecture/native-artifacts.md`](../../docs/architecture/native-artifacts.md).

## ABI

[`BppReplayMediaFoundation.h`](./BppReplayMediaFoundation.h) is the stable C boundary consumed by
`WindowsMediaFoundationVideoEncoder` and `WindowsNativeReplayAudioMuxer`. The render callback packet
and encoder handles are opaque across that boundary.

## Where it ships

The DLL must be installed in `TheBazaar_Data/Plugins/x86_64` so Unity loads it before BepInEx
constructs the managed recording backend.
