// Narrow C ABI declarations follow the pinned FMOD 2.02.33 fmod.cs/fmod_studio.cs.
// DSP descriptions stay opaque and are supplied by the matching Resonance archive.
#import <AVFoundation/AVFoundation.h>
#import <UIKit/UIKit.h>
#include <map>
#include <mutex>
#include <cstring>
#include <cstdio>

#define EXPORT extern "C" __attribute__((visibility("default")))
extern "C" {
struct FMOD_DSP_DESCRIPTION;
int FMOD5_System_Create(void **, unsigned);
int FMOD5_System_Release(void *);
int FMOD5_System_GetVersion(void *, unsigned *);
int FMOD5_System_SetOutput(void *, int);
int FMOD5_System_Init(void *, int, unsigned, void *);
int FMOD5_System_GetNumPlugins(void *, int, int *);
int FMOD5_System_GetPluginHandle(void *, int, int, unsigned *);
int FMOD5_System_GetPluginInfo(void *, unsigned, int *, char *, int, unsigned *);
int FMOD5_System_RegisterDSP(void *, FMOD_DSP_DESCRIPTION *, unsigned *);
int FMOD5_System_CreateDSPByPlugin(void *, unsigned, void **);
int FMOD5_DSP_Release(void *);
int FMOD5_System_MixerSuspend(void *);
int FMOD5_System_MixerResume(void *);
int FMOD_Studio_System_Initialize(void *, int, unsigned, unsigned, void *);
int FMOD_Studio_System_Release(void *);
int FMOD_Studio_System_GetCoreSystem(void *, void **);
int FMOD_Studio_System_GetBankCount(void *, int *);
int FMOD5_System_GetChannelsPlaying(void *, int *, int *);
int FMOD5_System_GetMasterChannelGroup(void *, void **);
int FMOD5_ChannelGroup_GetDSPClock(void *, unsigned long long *, unsigned long long *);
FMOD_DSP_DESCRIPTION *FMOD_ResonanceAudioListener_GetDSPDescription();
FMOD_DSP_DESCRIPTION *FMOD_ResonanceAudioSoundfield_GetDSPDescription();
FMOD_DSP_DESCRIPTION *FMOD_ResonanceAudioSource_GetDSPDescription();
}
static constexpr int DSP_PLUGIN = 2, INVALID_PARAM = 31;
struct Plugin { const char *name; FMOD_DSP_DESCRIPTION *(*description)(); };
static const Plugin plugins[] = {
    {"Resonance Audio Listener", FMOD_ResonanceAudioListener_GetDSPDescription},
    {"Resonance Audio Soundfield", FMOD_ResonanceAudioSoundfield_GetDSPDescription},
    {"Resonance Audio Source", FMOD_ResonanceAudioSource_GetDSPDescription},
};
static int find_plugin(void *system, const char *name, unsigned *handle, bool *found) {
    int count = 0, result = FMOD5_System_GetNumPlugins(system, DSP_PLUGIN, &count);
    *found = false;
    if (result) return result;
    for (int i = 0; i < count; ++i) {
        unsigned version; int type; char text[128] = {};
        if ((result = FMOD5_System_GetPluginHandle(system, DSP_PLUGIN, i, handle))) return result;
        if ((result = FMOD5_System_GetPluginInfo(system, *handle, &type, text, sizeof(text), &version))) return result;
        if (strcmp(text, name) == 0) { *found = true; break; }
    }
    return 0;
}
EXPORT int bpp_fmod_register_resonance(void *system) {
    if (!system) return INVALID_PARAM;
    for (const auto &plugin : plugins) {
        unsigned handle = 0; bool found = false;
        int result = find_plugin(system, plugin.name, &handle, &found);
        if (!result && !found) result = FMOD5_System_RegisterDSP(system, plugin.description(), &handle);
        if (result) {
            fprintf(stderr, "FMOD_STATIC registration=fail plugin=%s result=%d\n", plugin.name, result);
            return result;
        }
    }
    fprintf(stderr, "FMOD_STATIC registration=pass plugins=3\n");
    return 0;
}

// Observe natively: the shipped Mac managed integration omitted iOS's callback.
// Keep reasons separate so becoming active never resumes a still-interrupted mixer.
struct AudioSystem { void *core; bool suspended; bool managedPaused; };
static std::mutex audio_lock;
static std::map<void *, AudioSystem> systems;
static bool background = false, interrupted = false;
static int reconcile_locked() {
    if (systems.empty()) return 0;
    bool needsActive = false;
    for (const auto &entry : systems)
        needsActive |= !background && !interrupted && !entry.second.managedPaused;
    if (needsActive) {
        NSError *error = nil;
        if (![[AVAudioSession sharedInstance] setActive:YES error:&error]) {
            fprintf(stderr, "FMOD_SESSION activate=fail code=%ld\n", (long)error.code);
            return 51; // ERR_OUTPUT_INIT; retry on the next notification, never busy-wait.
        }
    }
    int firstError = 0;
    for (auto &entry : systems) {
        auto &state = entry.second;
        bool suspend = background || interrupted || state.managedPaused;
        if (state.suspended == suspend) continue;
        int result = suspend ? FMOD5_System_MixerSuspend(state.core) : FMOD5_System_MixerResume(state.core);
        if (!result) state.suspended = suspend;
        else if (!firstError) firstError = result;
        fprintf(stderr, "FMOD_SESSION %s result=%d\n", suspend ? "suspend" : "resume", result);
    }
    return firstError;
}
static void install_observers() {
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        NSNotificationCenter *center = NSNotificationCenter.defaultCenter;
        [center addObserverForName:UIApplicationDidEnterBackgroundNotification object:nil queue:NSOperationQueue.mainQueue usingBlock:^(NSNotification *) {
            std::lock_guard<std::mutex> lock(audio_lock); background = true; reconcile_locked();
        }];
        [center addObserverForName:UIApplicationDidBecomeActiveNotification object:nil queue:NSOperationQueue.mainQueue usingBlock:^(NSNotification *) {
            std::lock_guard<std::mutex> lock(audio_lock); background = false; reconcile_locked();
        }];
        [center addObserverForName:AVAudioSessionInterruptionNotification object:nil queue:NSOperationQueue.mainQueue usingBlock:^(NSNotification *notification) {
            std::lock_guard<std::mutex> lock(audio_lock);
            interrupted = [notification.userInfo[AVAudioSessionInterruptionTypeKey] unsignedIntegerValue] == AVAudioSessionInterruptionTypeBegan;
            reconcile_locked();
        }];
        [center addObserverForName:AVAudioSessionMediaServicesWereResetNotification object:nil queue:NSOperationQueue.mainQueue usingBlock:^(NSNotification *) {
            std::lock_guard<std::mutex> lock(audio_lock);
            for (auto &entry : systems) {
                if (!entry.second.suspended && !FMOD5_System_MixerSuspend(entry.second.core)) entry.second.suspended = true;
            }
            reconcile_locked();
        }];
    });
}
EXPORT int bpp_fmod_studio_initialize(void *studio, int channels, unsigned studioFlags, unsigned flags, void *extra) {
    int result = FMOD_Studio_System_Initialize(studio, channels, studioFlags, flags, extra);
    if (result) return result;
    void *core = nullptr;
    result = FMOD_Studio_System_GetCoreSystem(studio, &core);
    if (result) return result;
    install_observers();
    std::lock_guard<std::mutex> lock(audio_lock);
    systems[studio] = {core, false, false};
    return 0;
}
// Preserve RuntimeManager.OnApplicationPause's bus pausing, but combine its
// mixer request with OS interruption/background reasons to avoid double resume.
static int managed_pause(void *core, bool pause) {
    std::lock_guard<std::mutex> lock(audio_lock);
    for (auto &entry : systems) {
        if (entry.second.core != core) continue;
        entry.second.managedPaused = pause;
        return reconcile_locked();
    }
    return pause ? FMOD5_System_MixerSuspend(core) : FMOD5_System_MixerResume(core);
}
EXPORT int bpp_fmod_mixer_suspend(void *core) { return managed_pause(core, true); }
EXPORT int bpp_fmod_mixer_resume(void *core) { return managed_pause(core, false); }
EXPORT int bpp_fmod_studio_release(void *studio) {
    std::lock_guard<std::mutex> lock(audio_lock);
    int result = FMOD_Studio_System_Release(studio);
    if (!result) systems.erase(studio);
    return result;
}
// Read-only diagnostics for the launcher's opt-in Simulator test channel.
EXPORT const char *bpp_fmod_snapshot() {
    static thread_local char text[512];
    std::lock_guard<std::mutex> lock(audio_lock);
    if (systems.empty()) return "{\"systems\":0}";
    const auto &entry = *systems.begin();
    int banks = 0, channels = 0, real = 0;
    void *master = nullptr; unsigned long long clock = 0, parent = 0;
    int result = FMOD_Studio_System_GetBankCount(entry.first, &banks);
    if (!result) result = FMOD5_System_GetChannelsPlaying(entry.second.core, &channels, &real);
    if (!result) result = FMOD5_System_GetMasterChannelGroup(entry.second.core, &master);
    if (!result) result = FMOD5_ChannelGroup_GetDSPClock(master, &clock, &parent);
    snprintf(text, sizeof(text),
             "{\"systems\":%zu,\"banks\":%d,\"channels\":%d,\"realChannels\":%d,\"dspClock\":%llu,\"suspended\":%s,\"result\":%d}",
             systems.size(), banks, channels, real, clock, entry.second.suspended ? "true" : "false", result);
    return text;
}
EXPORT int bpp_fmod_selftest() {
    void *system = nullptr; unsigned version = 0; int count = 0, repeated = 0, created = 0;
    int result = FMOD5_System_Create(&system, 0x00020233);
    if (!result) result = FMOD5_System_GetVersion(system, &version);
    if (!result && version != 0x00020233) result = INVALID_PARAM;
    if (!result) result = FMOD5_System_SetOutput(system, 2); // NOSOUND, no audio-device dependency.
    if (!result) result = FMOD5_System_Init(system, 32, 0, nullptr);
    if (!result) result = bpp_fmod_register_resonance(system);
    if (!result) result = FMOD5_System_GetNumPlugins(system, DSP_PLUGIN, &count);
    if (!result) result = bpp_fmod_register_resonance(system);
    if (!result) result = FMOD5_System_GetNumPlugins(system, DSP_PLUGIN, &repeated);
    if (!result && count != repeated) result = INVALID_PARAM;
    for (const auto &plugin : plugins) {
        if (result) break;
        unsigned handle; bool found; void *dsp = nullptr;
        result = find_plugin(system, plugin.name, &handle, &found);
        if (!result && !found) result = INVALID_PARAM;
        if (!result) result = FMOD5_System_CreateDSPByPlugin(system, handle, &dsp);
        if (!result) { ++created; result = FMOD5_DSP_Release(dsp); }
    }
    if (system) { int released = FMOD5_System_Release(system); if (!result) result = released; }
    fprintf(stderr, "FMOD_PREFLIGHT version=%08x dsp=%d idempotent=%s result=%d\n",
            version, created, count == repeated ? "pass" : "fail", result);
    return result == 0 && created == 3;
}
