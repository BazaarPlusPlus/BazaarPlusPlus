# BppReplayVideoToolbox

Native macOS backend for Combat Replay video recording. It keeps the real-time path inside the
Unity process and does not use ScreenCaptureKit or an external encoder process.

## Data path

1. `CommandBuffer.IssuePluginEventAndData` places a native callback after Unity's final render
   commands.
2. The callback converts the final Metal texture to an IOSurface-backed NV12 `CVPixelBuffer` on
   the GPU.
3. VideoToolbox encodes H.264 asynchronously and AVAssetWriter writes the silent first-pass MP4.
4. The existing CoreAudio process tap closes its PCM WAV at replay end.
5. `BppVtMuxAudio` copies the H.264 samples and uses AVFoundation to mix/resample the WAV input to
   stereo 48 kHz AAC in the final MP4.

No full-frame GPU readback, raw-video pipe, or Screen Recording permission is involved. Windows
uses the matching D3D11/Media Foundation native backend in `native/windows`.

## Build

```bash
./build.sh
```

The script requires macOS, Apple Silicon, Xcode Command Line Tools, and Node.js for the shared
native artifact catalog. It emits an ad-hoc signed and producer-verified bundle at:

```text
build/GfxPluginBppReplayVideoToolbox.bundle
```

Ordinary C# contributors do not need the native toolchain: `./run.sh publish` reuses the installer's
staged copy only when both the macOS input digest and the staged artifacts still match the manifest,
and otherwise rebuilds through this script and promotes the result. The freshness contract and the
full promotion sequence are in
[`docs/architecture/native-artifacts.md`](../../docs/architecture/native-artifacts.md).

To build into an explicit side-effect-free output directory:

```bash
./build.sh /absolute/output/directory
```

The bundle must be present under `TheBazaar.app/Contents/Plugins` before Unity starts so Unity calls
`UnityPluginLoad` and provides `IUnityGraphicsMetal`. Release installation and app re-signing belong
to the companion installer PR; this repository never accepts a Developer ID or notarization
credential.

## ABI

[`BppReplayVideoToolbox.h`](./BppReplayVideoToolbox.h) is the stable C boundary consumed by
`MacMetalVideoEncoder` and `MacNativeReplayAudioMuxer`. The render callback packet and encoder
handles are opaque across that boundary.

## Acceptance

- output reports `avg_frame_rate=60/1`;
- `dropped_frames=0` and repeated frames are at most 3%;
- recorded replay p95 frame time is within 1 ms or 5% of the same replay without recording;
- audio finishes as `full`;
- the final MP4 has valid H.264 and AAC tracks and plays normally;
- the packaged mod contains only the platform-native recorder plugin.

p99 remains diagnostic telemetry, not a release gate.
