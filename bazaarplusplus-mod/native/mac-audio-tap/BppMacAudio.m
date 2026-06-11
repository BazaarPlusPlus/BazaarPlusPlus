// BppMacAudio.m — CoreAudio process-tap capture wrapper for The Bazaar mod.
//
// Architecture (see docs/design/archive/2026-05-31-combat-replay-audio-macos-process-tap.md
// sections 4 & 5): the realtime IOProc, the lock-free SPSC FIFO and the
// planar->interleave fixup all live here in native code. The IOProc is a
// CoreAudio realtime thread; it must never malloc, lock, send an ObjC message,
// touch CF/Foundation, log, or make a blocking syscall. The C# side only calls
// Read on its own background thread, so no external realtime thread ever enters
// Mono.

#import <Foundation/Foundation.h>
#import <CoreAudio/CoreAudio.h>
#import <CoreAudio/CATapDescription.h>
#import <CoreAudio/AudioHardwareTapping.h>

#include <stdatomic.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#include "BppMacAudio.h"

// ---------------------------------------------------------------------------
// Lock-free single-producer / single-consumer ring of float. Mirrors the
// semantics of Game/CombatReplay/Audio/AudioRingBuffer.cs: each cursor is
// strictly single-writer. The producer (IOProc) owns writePos and NEVER reads
// readPos; the consumer (Read) owns readPos and, on overrun, fast-forwards its
// OWN cursor past the clobbered region so the newest audio always wins. Cursors
// are 64-bit monotonic; the physical slot is pos & mask.
// ---------------------------------------------------------------------------

#define BPP_FIFO_CAPACITY (1 << 18) // floats; power of two
#define BPP_FIFO_MASK (BPP_FIFO_CAPACITY - 1)
#define BPP_MAX_PLANES 64 // upper bound on non-interleaved channel planes per IOProc cycle

typedef struct {
    float *buf;                  // BPP_FIFO_CAPACITY floats, malloc'd in Start
    _Atomic int64_t writePos;    // producer-owned (IOProc)
    _Atomic int64_t readPos;     // consumer-owned (Read)
} BppFifo;

typedef struct {
    BppFifo fifo;
    AudioObjectID tapID;
    AudioObjectID aggDevID;
    AudioDeviceIOProcID procID;
    int channels;
} BppCtx;

// PRODUCER: block-push one IOProc cycle. Loads writePos relaxed (sole writer),
// fills slots in place, then publishes the new cursor once with a release store
// so the consumer never observes data ahead of the published position. On
// overrun the oldest slots are simply overwritten; the consumer accounts the
// drop on its side. Wait-free, allocation-free.
static inline void bpp_fifo_push_block(BppFifo *f, const float *src, uint32_t count) {
    int64_t w = atomic_load_explicit(&f->writePos, memory_order_relaxed);
    for (uint32_t i = 0; i < count; i++) {
        f->buf[w & BPP_FIFO_MASK] = src[i];
        w++;
    }
    atomic_store_explicit(&f->writePos, w, memory_order_release);
}

// Planar push: interleave `frames` frames of `ch` non-interleaved planes
// (planes[c] is the c-th channel plane) into LRLR... order, single release at
// the end.
static inline void bpp_fifo_push_planar(BppFifo *f, const float *const *planes,
                                        int ch, uint32_t frames) {
    int64_t w = atomic_load_explicit(&f->writePos, memory_order_relaxed);
    for (uint32_t fr = 0; fr < frames; fr++) {
        for (int c = 0; c < ch; c++) {
            f->buf[w & BPP_FIFO_MASK] = planes[c][fr];
            w++;
        }
    }
    atomic_store_explicit(&f->writePos, w, memory_order_release);
}

// CONSUMER: drain up to maxFloats into dst. Acquire-load writePos; relaxed-load
// our own readPos. On overrun (w - r > capacity) fast-forward our cursor to
// w - capacity. Copy n floats, publish readPos with a release store.
static int32_t bpp_fifo_read(BppFifo *f, float *dst, int32_t maxFloats) {
    if (dst == NULL || maxFloats <= 0)
        return 0;

    int64_t w = atomic_load_explicit(&f->writePos, memory_order_acquire);
    int64_t r = atomic_load_explicit(&f->readPos, memory_order_relaxed);

    if (w - r > BPP_FIFO_CAPACITY)
        r = w - BPP_FIFO_CAPACITY;

    int64_t avail = w - r;
    int32_t n = (int32_t)(avail < maxFloats ? avail : maxFloats);
    if (n <= 0)
        return 0;

    for (int32_t i = 0; i < n; i++)
        dst[i] = f->buf[(r + i) & BPP_FIFO_MASK];

    atomic_store_explicit(&f->readPos, r + n, memory_order_release);
    return n;
}

// ---------------------------------------------------------------------------
// CoreAudio plumbing
// ---------------------------------------------------------------------------

// REALTIME THREAD. No malloc/free, lock, ObjC message, CF/Foundation call,
// logging or blocking syscall in here. The tapped audio is the aggregate
// device INPUT.
static OSStatus BppIOProc(AudioObjectID inDevice,
                          const AudioTimeStamp *inNow,
                          const AudioBufferList *inInput,
                          const AudioTimeStamp *inInputTime,
                          AudioBufferList *outOutput,
                          const AudioTimeStamp *inOutputTime,
                          void *inClientData) {
    (void)inDevice;
    (void)inNow;
    (void)inInputTime;
    (void)outOutput;
    (void)inOutputTime;

    BppCtx *ctx = (BppCtx *)inClientData;
    if (ctx == NULL || inInput == NULL || inInput->mNumberBuffers == 0)
        return noErr;

    int ch = ctx->channels;
    if (ch <= 0)
        return noErr;

    const AudioBufferList *abl = inInput;
    uint32_t nbuf = abl->mNumberBuffers;

    // Decide the layout from the buffer list itself (authoritative for what the device
    // actually delivers this cycle):
    //   non-interleaved -> one channel plane per buffer (mNumberBuffers == channels,
    //                      each buffer carrying a single channel);
    //   interleaved     -> a single buffer of LRLR... frames.
    // Any other (nbuf, ch) shape is an unexpected layout: drop the cycle and leave the
    // FIFO untouched rather than feed the interleaver one plane (which would corrupt
    // channel order / pitch). A NULL plane (CoreAudio can hand these out for a silent or
    // disabled stream) is skipped so the realtime path never dereferences NULL.
    if ((int)nbuf == ch && ch <= BPP_MAX_PLANES && abl->mBuffers[0].mNumberChannels <= 1) {
        const float *planes[BPP_MAX_PLANES];
        for (int c = 0; c < ch; c++) {
            const float *p = (const float *)abl->mBuffers[c].mData;
            if (p == NULL)
                return noErr;
            planes[c] = p;
        }
        uint32_t frames = abl->mBuffers[0].mDataByteSize / (uint32_t)sizeof(float);
        bpp_fifo_push_planar(&ctx->fifo, planes, ch, frames);
    } else if (nbuf == 1) {
        const float *data = (const float *)abl->mBuffers[0].mData;
        if (data == NULL)
            return noErr;
        uint32_t nf = abl->mBuffers[0].mDataByteSize / (uint32_t)sizeof(float);
        bpp_fifo_push_block(&ctx->fifo, data, nf);
    }

    return noErr;
}

// Read a tap object property (UID / format) on the tap AudioObjectID.
static OSStatus bpp_get_tap_property(AudioObjectID tapID, AudioObjectPropertySelector sel,
                                     void *outData, UInt32 *ioDataSize) {
    AudioObjectPropertyAddress addr = {
        sel, kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain};
    return AudioObjectGetPropertyData(tapID, &addr, 0, NULL, ioDataSize, outData);
}

int32_t BppMacAudio_IsSupported(void) {
    // Version gate via NSProcessInfo (real product version), not
    // kern.osproductversion and not Darwin kernel number. Safe on any macOS.
    NSOperatingSystemVersion v = {15, 0, 0};
    return [[NSProcessInfo processInfo] isOperatingSystemAtLeastVersion:v] ? 1 : 0;
}

// Reverse-order teardown of whatever a Start managed to create. ctx is calloc'd, so any
// stage never reached is 0/NULL and its guard skips it — the same helper therefore serves
// every Start error path AND Stop, keeping the teardown order in exactly one place.
// AudioDeviceStop on a created-but-never-started IOProc (an error path) is a harmless no-op.
// The tap-destroy is a weak macOS 14.2 symbol, hence the availability annotation; callers
// reach it only on macOS 15+ (the >=15 gate).
API_AVAILABLE(macos(15.0))
static void bpp_destroy_ctx(BppCtx *ctx) {
    if (ctx == NULL)
        return;
    if (ctx->procID != NULL && ctx->aggDevID != 0) {
        AudioDeviceStop(ctx->aggDevID, ctx->procID);
        AudioDeviceDestroyIOProcID(ctx->aggDevID, ctx->procID);
    }
    if (ctx->aggDevID != 0)
        AudioHardwareDestroyAggregateDevice(ctx->aggDevID);
    if (ctx->tapID != 0)
        AudioHardwareDestroyProcessTap(ctx->tapID);
    free(ctx->fifo.buf);
    free(ctx);
}

// All of the tap-touching setup lives here, annotated so clang statically knows
// these weak-imported APIs (CATapDescription / AudioHardwareCreateProcessTap,
// macOS 12.0/13.0/14.2) are only ever reached on a new-enough OS. BppMacAudio_Start
// calls this solely from inside an `if (@available(macOS 15.0, *))` check, which
// mirrors the C# >=15 gate (design section 7) and keeps the dylib self-defensive:
// on older systems the weak symbols are NULL and must never be called.
API_AVAILABLE(macos(15.0))
static void *bpp_start_tap(int32_t *outSampleRate, int32_t *outChannels) {
    BppCtx *ctx = (BppCtx *)calloc(1, sizeof(BppCtx));
    if (ctx == NULL)
        return NULL;

    // 1. Translate getpid() -> process AudioObjectID.
    pid_t pid = getpid();
    AudioObjectID processObjID = 0;
    UInt32 procSize = sizeof(processObjID);
    AudioObjectPropertyAddress xlate = {kAudioHardwarePropertyTranslatePIDToProcessObject,
                                        kAudioObjectPropertyScopeGlobal,
                                        kAudioObjectPropertyElementMain};
    OSStatus st = AudioObjectGetPropertyData(kAudioObjectSystemObject, &xlate,
                                             sizeof(pid_t), &pid, &procSize, &processObjID);
    if (st != noErr || processObjID == 0) {
        bpp_destroy_ctx(ctx);
        return NULL;
    }

    // 2. Build the tap description (stereo mixdown of just this process) and tap.
    CATapDescription *desc =
        [[CATapDescription alloc] initStereoMixdownOfProcesses:@[ @(processObjID) ]];
    desc.muteBehavior = CATapUnmuted;
    desc.privateTap = YES;
    desc.name = @"BazaarPlusPlus Audio Tap";

    // Each CoreAudio object is latched into ctx immediately after a successful create so a
    // later failure routes through bpp_destroy_ctx, which tears down exactly what exists.
    AudioObjectID tapID = 0;
    st = AudioHardwareCreateProcessTap(desc, &tapID);
    if (st != noErr || tapID == 0) {
        bpp_destroy_ctx(ctx);
        return NULL;
    }
    ctx->tapID = tapID;

    // 3. Read the tap UID and wrap the tap in an aggregate device.
    CFStringRef tapUID = NULL;
    UInt32 uidSize = sizeof(tapUID);
    st = bpp_get_tap_property(tapID, kAudioTapPropertyUID, &tapUID, &uidSize);
    if (st != noErr || tapUID == NULL) {
        bpp_destroy_ctx(ctx);
        return NULL;
    }

    NSDictionary *aggDesc = @{
        @kAudioAggregateDeviceNameKey : @"BazaarPlusPlus Audio Tap",
        @kAudioAggregateDeviceUIDKey : @"com.bazaarplusplus.audiotap",
        @kAudioAggregateDeviceIsPrivateKey : @YES,
        @kAudioAggregateDeviceTapAutoStartKey : @YES,
        @kAudioAggregateDeviceTapListKey : @[ @{
            @kAudioSubTapUIDKey : (__bridge NSString *)tapUID,
            @kAudioSubTapDriftCompensationKey : @NO
        } ]
    };

    AudioObjectID aggDevID = 0;
    st = AudioHardwareCreateAggregateDevice((__bridge CFDictionaryRef)aggDesc, &aggDevID);
    CFRelease(tapUID); // the aggregate description has copied what it needs
    if (st != noErr || aggDevID == 0) {
        bpp_destroy_ctx(ctx);
        return NULL;
    }
    ctx->aggDevID = aggDevID;

    // 4. Read the mixed-down stream format off the tap.
    AudioStreamBasicDescription asbd;
    memset(&asbd, 0, sizeof(asbd));
    UInt32 fmtSize = sizeof(asbd);
    st = bpp_get_tap_property(tapID, kAudioTapPropertyFormat, &asbd, &fmtSize);
    if (st != noErr || asbd.mChannelsPerFrame == 0) {
        bpp_destroy_ctx(ctx);
        return NULL;
    }
    ctx->channels = (int)asbd.mChannelsPerFrame;
    if (outSampleRate)
        *outSampleRate = (int32_t)asbd.mSampleRate;
    if (outChannels)
        *outChannels = (int32_t)asbd.mChannelsPerFrame;

    // 5. Allocate the FIFO buffer (pre-allocated; zero malloc on the RT path).
    ctx->fifo.buf = (float *)malloc((size_t)BPP_FIFO_CAPACITY * sizeof(float));
    if (ctx->fifo.buf == NULL) {
        bpp_destroy_ctx(ctx);
        return NULL;
    }
    atomic_store_explicit(&ctx->fifo.writePos, 0, memory_order_relaxed);
    atomic_store_explicit(&ctx->fifo.readPos, 0, memory_order_relaxed);

    // 6. Install the IOProc and start the device.
    AudioDeviceIOProcID procID = NULL;
    st = AudioDeviceCreateIOProcID(aggDevID, BppIOProc, ctx, &procID);
    if (st != noErr || procID == NULL) {
        bpp_destroy_ctx(ctx);
        return NULL;
    }
    ctx->procID = procID;

    if (AudioDeviceStart(aggDevID, procID) != noErr) {
        bpp_destroy_ctx(ctx);
        return NULL;
    }

    return (void *)ctx; // ARC manages desc / NSArray / NSDictionary
}

void *BppMacAudio_Start(int32_t *outSampleRate, int32_t *outChannels) {
    if (@available(macOS 15.0, *))
        return bpp_start_tap(outSampleRate, outChannels);
    return NULL;
}

int32_t BppMacAudio_Read(void *handle, float *dst, int32_t maxFloats) {
    if (handle == NULL)
        return 0;
    return bpp_fifo_read(&((BppCtx *)handle)->fifo, dst, maxFloats);
}

void BppMacAudio_Stop(void *handle) {
    if (handle == NULL)
        return;
    // A non-NULL handle can only have come from a successful Start, which runs only on
    // macOS 15+, so the same teardown helper applies. The @available guard re-states that
    // for clang so the weak 14.2 tap-destroy symbol is never referenced unguarded.
    if (@available(macOS 15.0, *))
        bpp_destroy_ctx((BppCtx *)handle);
}
