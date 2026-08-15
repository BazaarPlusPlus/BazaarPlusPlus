#ifndef BPP_REPLAY_VIDEO_TOOLBOX_H
#define BPP_REPLAY_VIDEO_TOOLBOX_H

#include <stdint.h>

#if defined(__cplusplus)
extern "C"
{
#endif

typedef struct BppVtNativeStats
{
    int submittedFrames;
    int appendedFrames;
    int acquireMisses;
    int notReadyDrops;
    int encodeErrors;
    int maxInFlight;
} BppVtNativeStats;

int BppVtHasUnityMetalInterface(void);
int BppVtCanMuxAudio(void);
void *BppVtGetRenderEventFunc(void);
int BppVtCreate(
    const char *outputPath,
    int width,
    int height,
    int fps,
    int bitrateBitsPerSecond,
    void **handle);
int BppVtGetSlotCount(void *handle);
int BppVtAcquireSlot(void *handle, int *slotIndex);

// Render-event packet ownership. BppVtPrepareRenderEvent returns a packet holding two
// references: one owned by the caller, one owned by the render-event callback the caller is
// about to queue. They are released independently and in no guaranteed order — the callback
// may run before, during, or after the call that releases the caller's reference.
//
//   BppVtCommitRenderEvent  releases the caller's reference. Use once the event is queued.
//   BppVtCancelRenderEvent  releases the caller's reference and marks the packet so a
//                           callback that has not run yet returns early. Use to abandon an
//                           event that WAS queued; the callback still releases its own.
//   BppVtDiscardRenderEvent releases BOTH references. Use only for an event that was NEVER
//                           queued, because no callback will ever run to release the second.
//
// Swapping the last two is not benign: Discard on a queued event frees the packet while the
// render thread still holds it, and Cancel on an unqueued event leaks it.
void *BppVtPrepareRenderEvent(
    void *handle,
    int slotIndex,
    int64_t firstFrameIndex,
    int frameCount);
void BppVtCommitRenderEvent(void *eventData);
void BppVtCancelRenderEvent(void *eventData);
void BppVtDiscardRenderEvent(void *eventData);
void BppVtReleaseSlot(void *handle, int slotIndex);
int BppVtFinish(void *handle, int timeoutMs);
void BppVtDestroy(void *handle);
int BppVtIsFailed(void *handle);
void BppVtGetStats(void *handle, BppVtNativeStats *stats);
int BppVtCopyError(void *handle, char *buffer, int capacity);

int BppVtMuxAudio(
    const char *silentVideoPath,
    const char *const *wavPaths,
    int wavPathCount,
    const char *finalPath,
    int audioBitrateBitsPerSecond,
    int timeoutMs,
    char *errorBuffer,
    int errorCapacity);

#if defined(__cplusplus)
}
#endif

#endif
