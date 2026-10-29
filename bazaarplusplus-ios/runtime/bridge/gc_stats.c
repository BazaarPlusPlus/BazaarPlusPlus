// BPP_GC_STATS=1: logs every collection's stop-the-world pause, with heap sizes, through
// Mono's profiler GC events. Used to tell GC hitches from interpreter or engine cost.
#include <mach/mach_time.h>
#include <stdint.h>
#include <stdlib.h>
#include <mono/metadata/profiler.h>

void bpp_log(const char *fmt, ...);
extern int64_t mono_gc_get_heap_size(void);
extern int64_t mono_gc_get_used_size(void);

static uint64_t g_stop_at;
static int g_count;

static double ms_since(uint64_t t) {
    static mach_timebase_info_data_t tb;
    if (!tb.denom) mach_timebase_info(&tb);
    return (double)(mach_absolute_time() - t) * tb.numer / tb.denom / 1e6;
}

static void on_gc_event(MonoProfiler *prof, MonoProfilerGCEvent ev, uint32_t gen, mono_bool serial) {
    (void)prof; (void)gen; (void)serial;
    switch (ev) {
    case MONO_GC_EVENT_PRE_STOP_WORLD: g_stop_at = mach_absolute_time(); break;
    case MONO_GC_EVENT_POST_START_WORLD: {
        bpp_log("gc #%d t=%.3f pause=%.1fms heap=%lldMB used=%lldMB", ++g_count, ms_since(0) / 1e3, ms_since(g_stop_at),
               mono_gc_get_heap_size() >> 20, mono_gc_get_used_size() >> 20);
        break;
    }
    default: break;
    }
}

void bpp_gc_stats_init(void) {
    if (!getenv("BPP_GC_STATS")) return;
    MonoProfilerHandle h = mono_profiler_create(NULL);
    mono_profiler_set_gc_event_callback(h, on_gc_event);
    bpp_log("gc stats on");
}
