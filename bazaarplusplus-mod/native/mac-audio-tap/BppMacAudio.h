// BppMacAudio.h — C ABI for the BazaarPlusPlus macOS CoreAudio process-tap wrapper.
//
// The whole point of this dylib is to keep every CoreAudio interaction (the ObjC
// CATapDescription, the realtime IOProc, the planar->interleave fixup and a
// lock-free SPSC FIFO) on the native side, so the C# layer degenerates into the
// same pull loop it uses for WASAPI loopback and never drags the CoreAudio
// realtime thread into the Mono runtime.
//
// All four entry points are plain C (extern "C", default visibility). The C#
// side binds them by name via [DllImport("BppMacAudio")] — resolved on
// Unity-Mono through the lib{name}.dylib probe, exactly like libe_sqlite3.dylib.

#ifndef BPP_MAC_AUDIO_H
#define BPP_MAC_AUDIO_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// Safe to call on ANY macOS — touches only Foundation (NSProcessInfo), never a
// tap symbol. This IS the version gate: returns 1 on macOS >= 15, else 0.
int32_t BppMacAudio_IsSupported(void);

// Starts a private self-tap of the current process and an aggregate device with
// an IOProc feeding the internal FIFO. On success returns an opaque handle and
// writes the actual mixed-down format (typically 48000 / 2). Returns NULL on any
// failure, after tearing down whatever was already created (no leaks).
void* BppMacAudio_Start(int32_t* outSampleRate, int32_t* outChannels);

// Drains up to maxFloats interleaved floats from the FIFO into dst. Returns the
// number of floats actually copied (0 when empty). Non-blocking, wait-free.
int32_t BppMacAudio_Read(void* handle, float* dst, int32_t maxFloats);

// Tears down the IOProc, aggregate device and tap (reverse of Start) and frees
// the handle. Idempotent-safe against NULL; the C# side guards double calls.
void BppMacAudio_Stop(void* handle);

#ifdef __cplusplus
}
#endif

#endif // BPP_MAC_AUDIO_H
