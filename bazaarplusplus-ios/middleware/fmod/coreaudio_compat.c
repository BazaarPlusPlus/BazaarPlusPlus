// CoreAudio shims for Mac audio plugins (FMOD) running on iOS.
//
// macOS plugins open the HAL output unit (kAudioUnitSubType_HALOutput), which iOS does not have; they get
// RemoteIO instead, the iOS output unit with the same render-callback interface. RemoteIO always plays to the
// current route, so the HAL-only "select this device" property is accepted and ignored.
//
// BPP_AUDIO_DIAG=1 also logs every interposed call with its result.
#include <AudioToolbox/AudioToolbox.h>
#include <stdlib.h>
#include <string.h>

void bpp_log(const char *fmt, ...);

#define DYLD_INTERPOSE(_replacement, _replacee) \
    __attribute__((used)) static struct { const void *replacement; const void *replacee; } _interpose_##_replacee \
    __attribute__((section("__DATA,__interpose"))) = {(const void *)(unsigned long)&_replacement, (const void *)(unsigned long)&_replacee};

static int diag(void) {
    static int on = -1;
    if (on < 0) { const char *v = getenv("BPP_AUDIO_DIAG"); on = v && !strcmp(v, "1"); }
    return on;
}

static const char *fourcc(UInt32 v, char out[5]) {
    out[0] = (char)(v >> 24); out[1] = (char)(v >> 16); out[2] = (char)(v >> 8); out[3] = (char)v; out[4] = 0;
    return out;
}

static AudioComponent ur_AudioComponentFindNext(AudioComponent c, const AudioComponentDescription *d) {
    AudioComponent r = AudioComponentFindNext(c, d);
    if (!r && d->componentType == kAudioUnitType_Output && d->componentSubType == 'ahal') {
        AudioComponentDescription remote = *d;
        remote.componentSubType = kAudioUnitSubType_RemoteIO;
        r = AudioComponentFindNext(c, &remote);
    }
    char t[5], s[5];
    if (diag()) bpp_log("audio FindNext type=%s sub=%s -> %p", fourcc(d->componentType, t), fourcc(d->componentSubType, s), (void *)r);
    return r;
}
DYLD_INTERPOSE(ur_AudioComponentFindNext, AudioComponentFindNext)

static OSStatus ur_AudioComponentInstanceNew(AudioComponent c, AudioComponentInstance *out) {
    OSStatus r = AudioComponentInstanceNew(c, out);
    if (diag()) bpp_log("audio InstanceNew %p -> %d", (void *)c, (int)r);
    return r;
}
DYLD_INTERPOSE(ur_AudioComponentInstanceNew, AudioComponentInstanceNew)

enum { kHALOutputCurrentDevice = 2000 };  // kAudioOutputUnitProperty_CurrentDevice (macOS only)

static OSStatus ur_AudioUnitSetProperty(AudioUnit u, AudioUnitPropertyID id, AudioUnitScope sc, AudioUnitElement el, const void *data, UInt32 size) {
    OSStatus r = id == kHALOutputCurrentDevice ? noErr : AudioUnitSetProperty(u, id, sc, el, data, size);
    if (diag()) bpp_log("audio SetProperty id=%u scope=%u el=%u size=%u -> %d", id, sc, el, size, (int)r);
    return r;
}
DYLD_INTERPOSE(ur_AudioUnitSetProperty, AudioUnitSetProperty)

static OSStatus ur_AudioUnitGetProperty(AudioUnit u, AudioUnitPropertyID id, AudioUnitScope sc, AudioUnitElement el, void *data, UInt32 *size) {
    OSStatus r = AudioUnitGetProperty(u, id, sc, el, data, size);
    if (diag()) bpp_log("audio GetProperty id=%u scope=%u el=%u -> %d", id, sc, el, (int)r);
    return r;
}
DYLD_INTERPOSE(ur_AudioUnitGetProperty, AudioUnitGetProperty)

static OSStatus ur_AudioUnitInitialize(AudioUnit u) {
    OSStatus r = AudioUnitInitialize(u);
    if (diag()) bpp_log("audio Initialize -> %d", (int)r);
    return r;
}
DYLD_INTERPOSE(ur_AudioUnitInitialize, AudioUnitInitialize)

static OSStatus ur_AudioOutputUnitStart(AudioUnit u) {
    OSStatus r = AudioOutputUnitStart(u);
    if (diag()) bpp_log("audio OutputUnitStart -> %d", (int)r);
    return r;
}
DYLD_INTERPOSE(ur_AudioOutputUnitStart, AudioOutputUnitStart)

// The iOS SDK headers omit the HAL object API, which the simulator's CoreAudio still exports.
typedef UInt32 AudioObjectID;
typedef struct { UInt32 mSelector, mScope, mElement; } UrAudioObjectPropertyAddress;
extern OSStatus AudioObjectGetPropertyData(AudioObjectID, const UrAudioObjectPropertyAddress *, UInt32, const void *, UInt32 *, void *);

static OSStatus ur_AudioObjectGetPropertyData(AudioObjectID obj, const UrAudioObjectPropertyAddress *a, UInt32 qs, const void *q, UInt32 *size, void *data) {
    OSStatus r = AudioObjectGetPropertyData(obj, a, qs, q, size, data);
    char sel[5], sc[5];
    if (diag()) bpp_log("audio ObjectGetPropertyData obj=%u sel=%s scope=%s -> %d", obj, fourcc(a->mSelector, sel), fourcc(a->mScope, sc), (int)r);
    return r;
}
DYLD_INTERPOSE(ur_AudioObjectGetPropertyData, AudioObjectGetPropertyData)
