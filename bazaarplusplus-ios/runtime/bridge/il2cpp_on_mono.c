// il2cpp_on_mono: the il2cpp embedding API that Unity's iOS engine (UnityRuntime.framework)
// resolves with dlsym, implemented on Unity's Mono fork.
//
// Handle mapping: every il2cpp handle is the corresponding Mono pointer
// (Il2CppClass* = MonoClass*, MethodInfo* = MonoMethod*, Il2CppObject* = MonoObject*, ...).
// Il2CppGCHandle is a void* in il2cpp and a uint32 in Mono; it is carried as an integer.

#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <pthread.h>
#include <dlfcn.h>
#include <dirent.h>
#include <libgen.h>
#include <mach-o/dyld.h>
#include <stdarg.h>

#include <mono/jit/jit.h>
#include <mono/metadata/appdomain.h>
#include <mono/metadata/assembly.h>
#include <mono/metadata/class.h>
#include <mono/metadata/debug-helpers.h>
#include <mono/metadata/exception.h>
#include <mono/metadata/image.h>
#include <mono/metadata/loader.h>
#include <mono/metadata/metadata.h>
#include <mono/metadata/mono-config.h>
#include <mono/metadata/mono-gc.h>
#include <mono/metadata/object.h>
#include <mono/metadata/reflection.h>
#include <mono/metadata/threads.h>
#include <mono/metadata/tokentype.h>
#include <mono/utils/mono-publib.h>
#include <mono/utils/mono-logger.h>

#define API __attribute__((visibility("default"), used))

#define LOG(...) do { fprintf(stderr, "[il2cpp_on_mono] " __VA_ARGS__); fputc('\n', stderr); } while (0)

// Shared with the shim's other files (engine_hooks.c).
void bpp_gc_stats_init(void);

void bpp_log(const char *fmt, ...) {
    va_list ap;
    va_start(ap, fmt);
    fputs("[il2cpp_on_mono] ", stderr);
    vfprintf(stderr, fmt, ap);
    fputc('\n', stderr);
    va_end(ap);
}

// ---- Unity Mono fork extras (exported, not in the public headers) ----------------------------
typedef void (*ClassReportFunc)(MonoClass *klass, void *user_data);
typedef void (*GFuncLike)(void *data, void *user_data);
typedef struct _LivenessState LivenessState;
typedef void (*register_object_callback)(void **arr, int size, void *userdata);
typedef void *(*ReallocateArray)(void *ptr, int size, void *userdata);

extern void mono_unity_class_for_each(ClassReportFunc callback, void *user_data);
extern int mono_class_get_userdata_offset(void);
extern void mono_class_set_userdata(MonoClass *klass, void *userdata);
extern mono_bool mono_unity_class_is_inited(MonoClass *klass);
extern mono_bool mono_class_is_generic(MonoClass *klass);
extern mono_bool mono_class_is_blittable(MonoClass *klass);
extern mono_bool mono_class_is_inflated(MonoClass *klass);
extern mono_bool mono_unity_class_is_abstract(MonoClass *klass);
extern mono_bool mono_unity_class_is_interface(MonoClass *klass);
extern uint32_t mono_unity_class_get_data_size(MonoClass *klass);
extern void *mono_unity_vtable_get_static_field_data(MonoVTable *vtable);
extern void mono_unity_type_get_name_full_chunked(MonoType *type, GFuncLike cb, void *user_data);
extern mono_bool mono_unity_type_is_static(MonoType *type);
extern mono_bool mono_unity_type_is_pointer_type(MonoType *type);
extern mono_bool mono_unity_class_field_is_literal(MonoClassField *field);
extern uint32_t mono_unity_object_header_size(void);
extern uint32_t mono_unity_array_object_header_size(void);
extern uint32_t mono_unity_offset_of_array_length_in_array_object_header(void);
extern uint32_t mono_unity_offset_of_array_bounds_in_array_object_header(void);
extern uint32_t mono_unity_allocation_granularity(void);
extern void mono_unity_runtime_set_main_args(int argc, const char *argv[]);
extern void mono_unity_gc_set_mode(int mode);
extern void mono_unity_gc_enable(void);
extern void mono_unity_gc_disable(void);
extern int mono_unity_gc_is_disabled(void);
extern void mono_unity_gc_handles_foreach_get_target(GFuncLike cb, void *user_data);
extern void mono_unity_gc_heap_foreach(GFuncLike cb, void *user_data);
extern void mono_unity_stop_gc_world(void);
extern void mono_unity_start_gc_world(void);
extern void mono_unity_install_memory_callbacks(MonoAllocatorVTable *callbacks);
extern void mono_unity_install_unitytls_interface(const void *callbacks);
extern void mono_domain_set_config(MonoDomain *domain, const char *base_dir, const char *config_file_name);
extern void mono_unity_set_data_dir(const char *dir);
extern mono_bool unity_mono_method_is_inflated(MonoMethod *method);
extern LivenessState *mono_unity_liveness_allocate_struct(MonoClass *filter, unsigned max_count, register_object_callback cb, void *userdata, ReallocateArray realloc_cb);
extern void mono_unity_liveness_calculation_from_root(MonoObject *root, LivenessState *state);
extern void mono_unity_liveness_calculation_from_statics(LivenessState *state);
extern void mono_unity_liveness_finalize(LivenessState *state);
extern void mono_unity_liveness_free_struct(LivenessState *state);
extern MonoArray *mono_unity_custom_attrs_construct(MonoCustomAttrInfo *cinfo, void *error);
extern MonoCustomAttrInfo *mono_custom_attrs_from_class(MonoClass *klass);
extern MonoCustomAttrInfo *mono_custom_attrs_from_method(MonoMethod *method);
extern MonoCustomAttrInfo *mono_custom_attrs_from_field(MonoClass *klass, MonoClassField *field);
extern mono_bool mono_custom_attrs_has_attr(MonoCustomAttrInfo *ainfo, MonoClass *attr_klass);
extern MonoObject *mono_custom_attrs_get_attr(MonoCustomAttrInfo *ainfo, MonoClass *attr_klass);
extern void mono_custom_attrs_free(MonoCustomAttrInfo *ainfo);
extern MonoArray *mono_custom_attrs_construct(MonoCustomAttrInfo *cinfo);
extern int64_t mono_gc_get_used_size(void);
extern int64_t mono_gc_get_heap_size(void);
extern void mono_gc_wbarrier_set_field(MonoObject *obj, void *field_ptr, MonoObject *value);
extern void mono_gc_wbarrier_generic_store(void *ptr, MonoObject *value);
extern mono_bool mono_gc_is_finalizer_thread(MonoThread *thread);
extern void *mono_gc_alloc_fixed_no_descriptor(size_t size, int source, void *key, const char *msg);
extern void mono_gc_free_fixed(void *addr);
extern void mono_runtime_unhandled_exception_policy_set(int policy);
typedef int (*vprintf_func)(const char *msg, va_list args);
extern void mono_unity_set_vprintf_func(vprintf_func func);
extern void mono_set_find_plugin_callback(const char *(*cb)(const char *));

// Optional Boehm incremental hooks; looked up lazily because not every build exports them.
static void *opt_sym(const char *name) { return dlsym(RTLD_DEFAULT, name); }

// ---- il2cpp types we need the layout of -------------------------------------------------------
typedef uint16_t Il2CppChar;
typedef void (*Il2CppMethodPointer)(void);
typedef struct {
    const MonoMethod *method;
    uintptr_t raw_ip;
    int sourceCodeLineNumber;
    int ilOffset;
    const char *filePath;
} Il2CppStackFrameInfo;
typedef void (*Il2CppFrameWalkFunc)(const Il2CppStackFrameInfo *info, void *user_data);
typedef struct {
    void *(*malloc_func)(size_t size);
    void *(*aligned_malloc_func)(size_t size, size_t alignment);
    void (*free_func)(void *ptr);
    void (*aligned_free_func)(void *ptr);
    void *(*calloc_func)(size_t nmemb, size_t size);
    void *(*realloc_func)(void *ptr, size_t size);
    void *(*aligned_realloc_func)(void *ptr, size_t size, size_t alignment);
} Il2CppMemoryCallbacks;
typedef struct {
    Il2CppMethodPointer methodPointer;
    int32_t code_size;
    const char *file;
    int line;
} Il2CppMethodDebugInfo;

// ---- runtime state ----------------------------------------------------------------------------
static MonoDomain *g_domain;
static char g_data_dir[1024];
static char g_managed_dir[1024];
static char g_config_dir[1024];
static int g_argc;
static const char **g_argv;
static size_t g_pool_region_size = 64 * 1024;

#define MAX_ASSEMBLIES 1024
static MonoAssembly *g_assemblies[MAX_ASSEMBLIES];
static size_t g_assembly_count;

typedef struct Icall { char *name; Il2CppMethodPointer fn; struct Icall *next; } Icall;
#define ICALL_BUCKETS 4096
static Icall *g_icalls[ICALL_BUCKETS];
static pthread_mutex_t g_icall_lock = PTHREAD_MUTEX_INITIALIZER;

static MonoDomain *domain(void) { return g_domain ? g_domain : mono_domain_get(); }

static unsigned hash_str(const char *s) { unsigned h = 5381; while (*s) h = h * 33 + (unsigned char)*s++; return h; }

static void bundle_dir(char *out, size_t size) {
    char exe[1024]; uint32_t len = sizeof(exe);
    if (_NSGetExecutablePath(exe, &len) != 0) { out[0] = 0; return; }
    snprintf(out, size, "%s", dirname(exe));
}

static void remember_assembly(MonoAssembly *a) {
    if (!a) return;
    for (size_t i = 0; i < g_assembly_count; i++) if (g_assemblies[i] == a) return;
    if (g_assembly_count < MAX_ASSEMBLIES) g_assemblies[g_assembly_count++] = a;
}

static void collect_assembly(MonoAssembly *a, void *ud) { (void)ud; remember_assembly(a); }

static MonoAssembly *open_assembly(const char *name) {
    char base[512];
    snprintf(base, sizeof(base), "%s", name);
    size_t n = strlen(base);
    if (n > 4 && (strcmp(base + n - 4, ".dll") == 0 || strcmp(base + n - 4, ".exe") == 0)) base[n - 4] = 0;
    for (size_t i = 0; i < g_assembly_count; i++) {
        const char *an = mono_image_get_name(mono_assembly_get_image(g_assemblies[i]));
        if (an && strcmp(an, base) == 0) return g_assemblies[i];
    }
    char path[2048];
    snprintf(path, sizeof(path), "%s/%s.dll", g_managed_dir, base);
    MonoAssembly *a = mono_domain_assembly_open(domain(), path);
    if (!a) {
        MonoImageOpenStatus st;
        MonoAssemblyName *an = mono_assembly_name_new(base);
        a = mono_assembly_load(an, NULL, &st);
        mono_assembly_name_free(an);
    }
    remember_assembly(a);
    return a;
}

// Loads every assembly listed in Data/ScriptingAssemblies.json; IL2CPP has them all resident after init.
static void preload_scripting_assemblies(void) {
    char path[2048];
    snprintf(path, sizeof(path), "%s/ScriptingAssemblies.json", g_data_dir);
    FILE *f = fopen(path, "rb");
    if (!f) { LOG("no %s", path); return; }
    fseek(f, 0, SEEK_END); long size = ftell(f); fseek(f, 0, SEEK_SET);
    char *buf = malloc(size + 1); fread(buf, 1, size, f); buf[size] = 0; fclose(f);
    char *names = strstr(buf, "\"names\"");
    char *end = names ? strchr(names, ']') : NULL;
    int loaded = 0, failed = 0;
    for (char *p = names ? strchr(names, '[') : NULL; p && p < end; ) {
        char *q = strchr(p, '"'); if (!q || q > end) break;
        char *r = strchr(q + 1, '"'); if (!r) break;
        *r = 0;
        if (open_assembly(q + 1)) loaded++; else { failed++; LOG("failed to load %s", q + 1); }
        p = r + 1;
    }
    free(buf);
    mono_assembly_foreach((MonoFunc)collect_assembly, NULL);
    LOG("preloaded %d assemblies (%d failed), %zu resident", loaded, failed, g_assembly_count);
}

// ---- init / config ----------------------------------------------------------------------------
API void il2cpp_set_data_dir(const char *p) {
    // IL2CPP is handed Data/Managed (metadata lives in Data/Managed/Metadata); the Mono layout uses Data.
    snprintf(g_data_dir, sizeof(g_data_dir), "%s", p);
    const char *suffixes[] = { "/Managed", "/il2cpp_data" };
    for (size_t i = 0; i < sizeof(suffixes) / sizeof(suffixes[0]); i++) {
        size_t n = strlen(g_data_dir), m = strlen(suffixes[i]);
        if (n > m && strcmp(g_data_dir + n - m, suffixes[i]) == 0) g_data_dir[n - m] = 0;
    }
}
API void il2cpp_set_config_dir(const char *p) { snprintf(g_config_dir, sizeof(g_config_dir), "%s", p); }
API void il2cpp_set_temp_dir(const char *p) { (void)p; }
API void il2cpp_set_config(const char *exe) { (void)exe; }
API void il2cpp_set_config_utf16(const Il2CppChar *exe) { (void)exe; }
API void il2cpp_set_commandline_arguments(int argc, const char *const argv[], const char *basedir) {
    (void)basedir; g_argc = argc; g_argv = (const char **)argv;
}
API void il2cpp_set_commandline_arguments_utf16(int argc, const Il2CppChar *const argv[], const char *basedir) {
    (void)argc; (void)argv; (void)basedir;
}

static Il2CppMemoryCallbacks g_mem_cb;
static void *mem_malloc(size_t s) { return g_mem_cb.malloc_func(s); }
static void *mem_realloc(void *p, size_t s) { return g_mem_cb.realloc_func(p, s); }
static void mem_free(void *p) { g_mem_cb.free_func(p); }
static void *mem_calloc(size_t n, size_t s) { return g_mem_cb.calloc_func(n, s); }
static MonoAllocatorVTable g_mono_alloc = { MONO_ALLOCATOR_VTABLE_VERSION, mem_malloc, mem_realloc, mem_free, mem_calloc };
API void il2cpp_set_memory_callbacks(Il2CppMemoryCallbacks *cb) {
    // An embedded service may already own Mono allocations before Unity starts.
    if (g_domain) return;
    if (!cb || !cb->malloc_func || !cb->free_func || !cb->calloc_func || !cb->realloc_func) return;
    g_mem_cb = *cb;
    mono_unity_install_memory_callbacks(&g_mono_alloc);
}
API void il2cpp_memory_pool_set_region_size(size_t s) { g_pool_region_size = s; }
API size_t il2cpp_memory_pool_get_region_size(void) { return g_pool_region_size; }

// The engine registers its icalls before il2cpp_init; Mono's icall table exists only after init.
static void register_pending_icalls(void) {
    int n = 0;
    pthread_mutex_lock(&g_icall_lock);
    for (int b = 0; b < ICALL_BUCKETS; b++)
        for (Icall *ic = g_icalls[b]; ic; ic = ic->next) { mono_add_internal_call(ic->name, (const void *)ic->fn); n++; }
    pthread_mutex_unlock(&g_icall_lock);
    LOG("registered %d engine icalls", n);
}

static int log_vprintf(const char *msg, va_list args) { return vfprintf(stderr, msg, args); }
static void log_mono(const char *domain, const char *level, const char *message, mono_bool fatal, void *data) {
    LOG("Mono %s: %s", level, message);
    if (fatal) abort();
}

// The game's native plugins, retargeted to iOS, live in <bundle>/Plugins/<name>.dylib. Map both the
// DllImport name and its lib-prefixed form to the file, as the Mac player's plugin lookup would.
static void map_bundle_plugins(const char *bundle) {
    char dir[1024], path[2048], name[256];
    snprintf(dir, sizeof(dir), "%s/Plugins", bundle);
    DIR *d = opendir(dir);
    if (!d) return;
    for (struct dirent *e; (e = readdir(d));) {
        size_t n = strlen(e->d_name);
        if (n <= 6 || n - 6 >= sizeof(name) - 3 || strcmp(e->d_name + n - 6, ".dylib") != 0) continue;
        snprintf(path, sizeof(path), "%s/%s", dir, e->d_name);
        snprintf(name, sizeof(name), "%.*s", (int)(n - 6), e->d_name);
        mono_dllmap_insert(NULL, name, NULL, path, NULL);
        char prefixed[260];
        snprintf(prefixed, sizeof(prefixed), "lib%s", name);
        mono_dllmap_insert(NULL, prefixed, NULL, path, NULL);
        LOG("plugin %s -> %s", name, path);
    }
    closedir(d);
}

API int il2cpp_init(const char *domain_name) {
    mono_unity_set_vprintf_func(log_vprintf);
    mono_trace_set_log_handler(log_mono, NULL);
    char bundle[1024];
    bundle_dir(bundle, sizeof(bundle));
    if (!g_data_dir[0]) snprintf(g_data_dir, sizeof(g_data_dir), "%s/Data", bundle);
    snprintf(g_managed_dir, sizeof(g_managed_dir), "%s/Managed", g_data_dir);
    if (!g_config_dir[0]) snprintf(g_config_dir, sizeof(g_config_dir), "%s/Mono/etc", bundle);

    if (g_domain) {
        mono_thread_attach(g_domain);
        mono_set_assemblies_path(g_managed_dir);
        mono_unity_set_data_dir(g_data_dir);
        char config[1100];
        snprintf(config, sizeof(config), "%s/app.config", g_data_dir);
        mono_domain_set_config(g_domain, g_data_dir, config);
        register_pending_icalls();
        if (g_argc > 0) mono_unity_runtime_set_main_args(g_argc, g_argv);
        preload_scripting_assemblies();
        LOG("reusing embedded service domain for Unity");
        return 1;
    }

    const char *mode = getenv("BPP_MONO_MODE");
    extern void *mono_aot_module_mscorlib_info;
    mono_aot_register_module((void **)mono_aot_module_mscorlib_info);
    mono_jit_set_aot_mode(MONO_AOT_MODE_INTERP);
    mode = "static-aot-interp";
    LOG("init domain=%s data=%s mode=%s", domain_name, g_data_dir, mode ? mode : "interp");

    // Boehm only runs Mono's ephemeron hook (mono_push_ephemerons) on its serial mark path; with parallel
    // markers, values in ConditionalWeakTables are never marked and get freed under live keys.
    setenv("GC_MARKERS", "1", 1);
    mono_set_dirs(g_managed_dir, g_config_dir);
    mono_set_assemblies_path(g_managed_dir);
    mono_unity_set_data_dir(g_data_dir);
    mono_config_parse(NULL);
    // The statically linked native shims and stubs/ live in this dylib, which is not the main executable that
    // "__Internal" resolves against, so map them to our own path.
    Dl_info self;
    if (dladdr((void *)il2cpp_init, &self) && self.dli_fname) {
        static const char *const own_libs[] = {"System.Native", "libSystem.Native", "MonoPosixHelper",
            "System.Net.Security.Native", "System.Security.Cryptography.Native.Apple", "__Internal",
            "steam_api", "libsteam_api"};
        for (size_t i = 0; i < sizeof(own_libs) / sizeof(own_libs[0]); i++)
            mono_dllmap_insert(NULL, own_libs[i], NULL, self.dli_fname, NULL);
        const char *static_plugin_libs[] = {"gilzoide-sqlite-net", "libgilzoide-sqlite-net", "sqlite3", "libsqlite3", "sqlite3.dll", "e_sqlite3", "libe_sqlite3", "abci", "libabci",
            "fmodstudio", "libfmodstudio", "fmodstudiounityplugin", "libfmodstudiounityplugin"};
        for (size_t i = 0; i < sizeof(static_plugin_libs) / sizeof(static_plugin_libs[0]); i++)
            mono_dllmap_insert(NULL, static_plugin_libs[i], NULL, self.dli_fname, NULL);
    }
    map_bundle_plugins(bundle);
    g_domain = mono_jit_init_version(domain_name ? domain_name : "IL2CPP Root Domain", "v4.0.30319");
    if (!g_domain) { LOG("mono_jit_init_version failed"); return 0; }
    bpp_gc_stats_init();
    // As Unity's Mono player does: without an application config file, System.Configuration throws and
    // takes ServicePointManager (and so every managed HTTPS request) down with it. The file need not exist.
    char app_config[1100];
    snprintf(app_config, sizeof(app_config), "%s/app.config", g_data_dir);
    mono_domain_set_config(g_domain, g_data_dir, app_config);
    register_pending_icalls();
    if (g_argc > 0) mono_unity_runtime_set_main_args(g_argc, g_argv);
    preload_scripting_assemblies();
    return 1;
}
API int il2cpp_init_utf16(const Il2CppChar *domain_name) { (void)domain_name; return il2cpp_init("IL2CPP Root Domain"); }
API void il2cpp_shutdown(void) { if (g_domain) mono_jit_cleanup(g_domain); }

API const MonoImage *il2cpp_get_corlib(void) { return mono_get_corlib(); }

API void il2cpp_add_internal_call(const char *name, Il2CppMethodPointer fn) {
    if (g_domain) mono_add_internal_call(name, (const void *)fn);
    Icall *ic = malloc(sizeof(Icall));
    ic->name = strdup(name); ic->fn = fn;
    unsigned b = hash_str(name) % ICALL_BUCKETS;
    pthread_mutex_lock(&g_icall_lock);
    ic->next = g_icalls[b]; g_icalls[b] = ic;
    pthread_mutex_unlock(&g_icall_lock);
}
API Il2CppMethodPointer il2cpp_resolve_icall(const char *name) {
    unsigned b = hash_str(name) % ICALL_BUCKETS;
    pthread_mutex_lock(&g_icall_lock);
    Icall *ic = g_icalls[b];
    while (ic && strcmp(ic->name, name) != 0) ic = ic->next;
    pthread_mutex_unlock(&g_icall_lock);
    return ic ? ic->fn : NULL;
}

API void *il2cpp_alloc(size_t size) { return malloc(size); }
API void il2cpp_free(void *p) { free(p); }

// ---- arrays -----------------------------------------------------------------------------------
API MonoClass *il2cpp_array_class_get(MonoClass *e, uint32_t rank) { return mono_array_class_get(e, rank); }
API uint32_t il2cpp_array_length(MonoArray *a) { return (uint32_t)mono_array_length(a); }
API uint32_t il2cpp_array_get_byte_length(MonoArray *a) {
    return (uint32_t)(mono_array_length(a) * mono_array_element_size(mono_object_get_class((MonoObject *)a)));
}
API MonoArray *il2cpp_array_new(MonoClass *e, uintptr_t n) { return mono_array_new(domain(), e, n); }
API MonoArray *il2cpp_array_new_specific(MonoClass *ac, uintptr_t n) { return mono_array_new_specific(mono_class_vtable(domain(), ac), n); }
API MonoArray *il2cpp_array_new_full(MonoClass *ac, uintptr_t *lengths, intptr_t *lower) { return mono_array_new_full(domain(), ac, lengths, lower); }
API MonoClass *il2cpp_bounded_array_class_get(MonoClass *e, uint32_t rank, bool bounded) { return mono_bounded_array_class_get(e, rank, bounded); }
API int il2cpp_array_element_size(const MonoClass *ac) { return mono_array_element_size((MonoClass *)ac); }
API int il2cpp_class_array_element_size(const MonoClass *k) { return mono_class_array_element_size((MonoClass *)k); }

// ---- assembly / image / domain ----------------------------------------------------------------
API const MonoImage *il2cpp_assembly_get_image(const MonoAssembly *a) { return mono_assembly_get_image((MonoAssembly *)a); }
API MonoDomain *il2cpp_domain_get(void) { return domain(); }
API const MonoAssembly *il2cpp_domain_assembly_open(MonoDomain *d, const char *name) { (void)d; return open_assembly(name); }
API const MonoAssembly **il2cpp_domain_get_assemblies(const MonoDomain *d, size_t *size) {
    (void)d;
    mono_assembly_foreach((MonoFunc)collect_assembly, NULL);
    *size = g_assembly_count;
    return (const MonoAssembly **)g_assemblies;
}
API const MonoAssembly *il2cpp_image_get_assembly(const MonoImage *i) { return mono_image_get_assembly((MonoImage *)i); }

static const char *image_file_name(const MonoImage *i) {
    // IL2CPP image names carry the .dll extension; Mono's carry none.
    static __thread char buf[512];
    snprintf(buf, sizeof(buf), "%s.dll", mono_image_get_name((MonoImage *)i));
    return strdup(buf); // leaked once per call site; callers cache the result
}
API const char *il2cpp_image_get_name(const MonoImage *i) { return image_file_name(i); }
API const char *il2cpp_image_get_filename(const MonoImage *i) { return image_file_name(i); }
API const MonoMethod *il2cpp_image_get_entry_point(const MonoImage *i) {
    uint32_t tok = mono_image_get_entry_point((MonoImage *)i);
    return tok ? mono_get_method((MonoImage *)i, tok, NULL) : NULL;
}
API size_t il2cpp_image_get_class_count(const MonoImage *i) { return (size_t)mono_image_get_table_rows((MonoImage *)i, MONO_TABLE_TYPEDEF); }
API const MonoClass *il2cpp_image_get_class(const MonoImage *i, size_t index) {
    return mono_class_get((MonoImage *)i, MONO_TOKEN_TYPE_DEF | (uint32_t)(index + 1));
}

// ---- class ------------------------------------------------------------------------------------
API void il2cpp_class_for_each(void (*cb)(MonoClass *, void *), void *ud) { mono_unity_class_for_each(cb, ud); }
API const MonoType *il2cpp_class_enum_basetype(MonoClass *k) { return mono_class_enum_basetype(k); }
API bool il2cpp_class_is_inited(const MonoClass *k) { return mono_unity_class_is_inited((MonoClass *)k); }
API bool il2cpp_class_is_generic(const MonoClass *k) { return mono_class_is_generic((MonoClass *)k); }
API bool il2cpp_class_is_inflated(const MonoClass *k) { return mono_class_is_inflated((MonoClass *)k); }
API bool il2cpp_class_is_assignable_from(MonoClass *k, MonoClass *o) { return mono_class_is_assignable_from(k, o); }
API bool il2cpp_class_is_subclass_of(MonoClass *k, MonoClass *c, bool ifaces) { return mono_class_is_subclass_of(k, c, ifaces); }
API bool il2cpp_class_has_parent(MonoClass *k, MonoClass *c) { return k == c || mono_class_is_subclass_of(k, c, false); }
API MonoClass *il2cpp_class_from_il2cpp_type(const MonoType *t) { return mono_class_from_mono_type((MonoType *)t); }
API MonoClass *il2cpp_class_from_type(const MonoType *t) { return mono_class_from_mono_type((MonoType *)t); }
API MonoClass *il2cpp_class_from_name(const MonoImage *i, const char *ns, const char *n) { return mono_class_from_name((MonoImage *)i, ns, n); }
API MonoClass *il2cpp_class_from_system_type(MonoReflectionType *rt) { return mono_class_from_mono_type(mono_reflection_type_get_type(rt)); }
API MonoClass *il2cpp_class_get_element_class(MonoClass *k) { return mono_class_get_element_class(k); }
API MonoEvent *il2cpp_class_get_events(MonoClass *k, void **it) { return mono_class_get_events(k, it); }
API MonoClassField *il2cpp_class_get_fields(MonoClass *k, void **it) { return mono_class_get_fields(k, it); }
API MonoClass *il2cpp_class_get_nested_types(MonoClass *k, void **it) { return mono_class_get_nested_types(k, it); }
API MonoClass *il2cpp_class_get_interfaces(MonoClass *k, void **it) { return mono_class_get_interfaces(k, it); }
API MonoProperty *il2cpp_class_get_properties(MonoClass *k, void **it) { return mono_class_get_properties(k, it); }
API MonoProperty *il2cpp_class_get_property_from_name(MonoClass *k, const char *n) { return mono_class_get_property_from_name(k, n); }
API MonoClassField *il2cpp_class_get_field_from_name(MonoClass *k, const char *n) { return mono_class_get_field_from_name(k, n); }
API MonoMethod *il2cpp_class_get_methods(MonoClass *k, void **it) { return mono_class_get_methods(k, it); }
API MonoMethod *il2cpp_class_get_method_from_name(MonoClass *k, const char *n, int argc) { return mono_class_get_method_from_name(k, n, argc); }
API const char *il2cpp_class_get_name(MonoClass *k) { return mono_class_get_name(k); }
API void il2cpp_type_get_name_chunked(const MonoType *t, void (*cb)(void *, void *), void *ud) { mono_unity_type_get_name_full_chunked((MonoType *)t, cb, ud); }
API const char *il2cpp_class_get_namespace(MonoClass *k) { return mono_class_get_namespace(k); }
API MonoClass *il2cpp_class_get_parent(MonoClass *k) { return mono_class_get_parent(k); }
API MonoClass *il2cpp_class_get_declaring_type(MonoClass *k) { return mono_class_get_nesting_type(k); }
API int32_t il2cpp_class_instance_size(MonoClass *k) { return mono_class_instance_size(k); }
API size_t il2cpp_class_num_fields(const MonoClass *k) { return mono_class_num_fields((MonoClass *)k); }
API bool il2cpp_class_is_valuetype(const MonoClass *k) { return mono_class_is_valuetype((MonoClass *)k); }
API int32_t il2cpp_class_value_size(MonoClass *k, uint32_t *align) { return mono_class_value_size(k, align); }
API bool il2cpp_class_is_blittable(const MonoClass *k) { return mono_class_is_blittable((MonoClass *)k); }
API int il2cpp_class_get_flags(const MonoClass *k) { return (int)mono_class_get_flags((MonoClass *)k); }
API bool il2cpp_class_is_abstract(const MonoClass *k) { return mono_unity_class_is_abstract((MonoClass *)k); }
API bool il2cpp_class_is_interface(const MonoClass *k) { return mono_unity_class_is_interface((MonoClass *)k); }
API const MonoType *il2cpp_class_get_type(MonoClass *k) { return mono_class_get_type(k); }
API uint32_t il2cpp_class_get_type_token(MonoClass *k) { return mono_class_get_type_token(k); }
API bool il2cpp_class_has_attribute(MonoClass *k, MonoClass *attr) {
    MonoCustomAttrInfo *il2cpp_custom_attrs_from_class(MonoClass *k);
    MonoCustomAttrInfo *ai = il2cpp_custom_attrs_from_class(k);
    return ai && mono_custom_attrs_has_attr(ai, attr);
}
API bool il2cpp_class_has_references(MonoClass *k) {
    if (!mono_class_is_valuetype(k)) return true;
    void *it = NULL; MonoClassField *f;
    while ((f = mono_class_get_fields(k, &it))) {
        MonoType *t = mono_field_get_type(f);
        if (mono_field_get_flags(f) & 0x10 /* FIELD_ATTRIBUTE_STATIC */) continue;
        int tt = mono_type_get_type(t);
        if (mono_type_is_reference(t)) return true;
        if (tt == MONO_TYPE_VALUETYPE || tt == MONO_TYPE_GENERICINST) {
            MonoClass *fk = mono_class_from_mono_type(t);
            if (fk != k && mono_class_is_valuetype(fk) && il2cpp_class_has_references(fk)) return true;
        }
    }
    return false;
}
API bool il2cpp_class_is_enum(const MonoClass *k) { return mono_class_is_enum((MonoClass *)k); }
API const MonoImage *il2cpp_class_get_image(MonoClass *k) { return mono_class_get_image(k); }
API const char *il2cpp_class_get_assemblyname(const MonoClass *k) { return mono_image_get_name(mono_class_get_image((MonoClass *)k)); }
API int il2cpp_class_get_rank(const MonoClass *k) { return mono_class_get_rank((MonoClass *)k); }
API uint32_t il2cpp_class_get_data_size(const MonoClass *k) { return mono_unity_class_get_data_size((MonoClass *)k); }
API void *il2cpp_class_get_static_field_data(const MonoClass *k) {
    MonoVTable *vt = mono_class_vtable(domain(), (MonoClass *)k);
    return vt ? mono_unity_vtable_get_static_field_data(vt) : NULL;
}
API void il2cpp_class_set_userdata(MonoClass *k, void *ud) { mono_class_set_userdata(k, ud); }
API int il2cpp_class_get_userdata_offset(void) { return mono_class_get_userdata_offset(); }

// ---- stats ------------------------------------------------------------------------------------
API bool il2cpp_stats_dump_to_file(const char *path) { (void)path; return false; }
API uint64_t il2cpp_stats_get_value(int stat) { (void)stat; return 0; }

// ---- exceptions -------------------------------------------------------------------------------
API void il2cpp_raise_exception(MonoException *ex) { mono_raise_exception(ex); }
API MonoException *il2cpp_exception_from_name_msg(const MonoImage *i, const char *ns, const char *n, const char *msg) {
    return mono_exception_from_name_msg((MonoImage *)i, ns, n, msg);
}
API MonoException *il2cpp_get_exception_argument_null(const char *arg) { return mono_get_exception_argument_null(arg); }

static void copy_managed_string(MonoString *s, char *out, int size) {
    if (!out || size <= 0) return;
    out[0] = 0;
    if (!s) return;
    char *utf8 = mono_string_to_utf8(s);
    snprintf(out, size, "%s", utf8);
    mono_free(utf8);
}
static MonoString *exception_property(const MonoException *ex, const char *prop) {
    MonoClass *k = mono_object_get_class((MonoObject *)ex);
    MonoProperty *p = NULL;
    for (MonoClass *c = k; c && !p; c = mono_class_get_parent(c)) p = mono_class_get_property_from_name(c, prop);
    if (!p) return NULL;
    MonoObject *exc = NULL;
    MonoObject *v = mono_property_get_value(p, (void *)ex, NULL, &exc);
    return exc ? NULL : (MonoString *)v;
}
API void il2cpp_format_exception(const MonoException *ex, char *message, int size) {
    if (!ex || !message || size <= 0) return;
    MonoClass *k = mono_object_get_class((MonoObject *)ex);
    char msg[4096];
    copy_managed_string(exception_property(ex, "Message"), msg, sizeof(msg));
    const char *ns = mono_class_get_namespace(k);
    snprintf(message, size, "%s%s%s: %s", ns, ns[0] ? "." : "", mono_class_get_name(k), msg);
}
API void il2cpp_format_stack_trace(const MonoException *ex, char *out, int size) {
    copy_managed_string(ex ? exception_property(ex, "StackTrace") : NULL, out, size);
}
API void il2cpp_unhandled_exception(MonoException *ex) { mono_unhandled_exception((MonoObject *)ex); }
API void il2cpp_native_stack_trace(const MonoException *ex, uintptr_t **addresses, int *num, char **uuid, char **image) {
    (void)ex; if (addresses) *addresses = NULL; if (num) *num = 0; if (uuid) *uuid = NULL; if (image) *image = NULL;
}

// ---- fields -----------------------------------------------------------------------------------
API int il2cpp_field_get_flags(MonoClassField *f) { return (int)mono_field_get_flags(f); }
API const MonoClassField *il2cpp_field_get_from_reflection(const MonoReflectionField *rf) {
    // MonoReflectionField { MonoObject object; MonoClass *klass; MonoClassField *field; ... }
    return ((MonoClassField *const *)((const char *)rf + sizeof(MonoObject)))[1];
}
API const char *il2cpp_field_get_name(MonoClassField *f) { return mono_field_get_name(f); }
API MonoClass *il2cpp_field_get_parent(MonoClassField *f) { return mono_field_get_parent(f); }
API MonoReflectionField *il2cpp_field_get_object(MonoClassField *f, MonoClass *refclass) {
    return mono_field_get_object(domain(), refclass ? refclass : mono_field_get_parent(f), f);
}
API size_t il2cpp_field_get_offset(MonoClassField *f) { return mono_field_get_offset(f); }
API const MonoType *il2cpp_field_get_type(MonoClassField *f) { return mono_field_get_type(f); }
API void il2cpp_field_get_value(MonoObject *o, MonoClassField *f, void *v) { mono_field_get_value(o, f, v); }
API MonoObject *il2cpp_field_get_value_object(MonoClassField *f, MonoObject *o) { return mono_field_get_value_object(domain(), f, o); }
API bool il2cpp_field_has_attribute(MonoClassField *f, MonoClass *attr) {
    MonoCustomAttrInfo *il2cpp_custom_attrs_from_field(const MonoClassField *f);
    MonoCustomAttrInfo *ai = il2cpp_custom_attrs_from_field(f);
    return ai && mono_custom_attrs_has_attr(ai, attr);
}
API void il2cpp_field_set_value(MonoObject *o, MonoClassField *f, void *v) { mono_field_set_value(o, f, v); }
API void il2cpp_field_static_get_value(MonoClassField *f, void *v) {
    mono_field_static_get_value(mono_class_vtable(domain(), mono_field_get_parent(f)), f, v);
}
API void il2cpp_field_static_set_value(MonoClassField *f, void *v) {
    mono_field_static_set_value(mono_class_vtable(domain(), mono_field_get_parent(f)), f, v);
}
API void il2cpp_field_set_value_object(MonoObject *o, MonoClassField *f, MonoObject *v) {
    MonoType *t = mono_field_get_type(f);
    if (!mono_type_is_reference(t) && v) mono_field_set_value(o, f, mono_object_unbox(v));
    else mono_field_set_value(o, f, &v);
}
API bool il2cpp_field_is_literal(MonoClassField *f) { return mono_unity_class_field_is_literal(f); }

// ---- GC ---------------------------------------------------------------------------------------
API void il2cpp_gc_collect(int maxgen) { mono_gc_collect(maxgen); }
API int32_t il2cpp_gc_collect_a_little(void) {
    int (*fn)(void) = (int (*)(void))opt_sym("mono_gc_collect_a_little");
    return fn ? fn() : 0;
}
API void il2cpp_gc_start_incremental_collection(void) {
    void (*fn)(void) = (void (*)(void))opt_sym("mono_gc_start_incremental_collection");
    if (fn) fn();
}
API void il2cpp_gc_disable(void) { mono_unity_gc_disable(); }
API void il2cpp_gc_enable(void) { mono_unity_gc_enable(); }
API bool il2cpp_gc_is_disabled(void) { return mono_unity_gc_is_disabled() != 0; }
API void il2cpp_gc_set_mode(int mode) { mono_unity_gc_set_mode(mode); }
API int64_t il2cpp_gc_get_max_time_slice_ns(void) {
    int64_t (*fn)(void) = (int64_t (*)(void))opt_sym("mono_gc_get_max_time_slice_ns");
    return fn ? fn() : 0;
}
API void il2cpp_gc_set_max_time_slice_ns(int64_t ns) {
    void (*fn)(int64_t) = (void (*)(int64_t))opt_sym("mono_gc_set_max_time_slice_ns");
    if (fn) fn(ns);
}
API bool il2cpp_gc_is_incremental(void) {
    unsigned char (*fn)(void) = (unsigned char (*)(void))opt_sym("mono_gc_is_incremental");
    return fn ? fn() != 0 : false;
}
API int64_t il2cpp_gc_get_used_size(void) { return mono_gc_get_used_size(); }
API int64_t il2cpp_gc_get_heap_size(void) { return mono_gc_get_heap_size(); }
API void il2cpp_gc_wbarrier_set_field(MonoObject *o, void **target, void *value) { mono_gc_wbarrier_set_field(o, target, (MonoObject *)value); }
API bool il2cpp_gc_has_strict_wbarriers(void) { return false; }
API void il2cpp_gc_set_external_allocation_tracker(void (*fn)(void *, size_t, int)) { (void)fn; }
API void il2cpp_gc_set_external_wbarrier_tracker(void (*fn)(void **)) { (void)fn; }
API void il2cpp_gc_foreach_heap(void (*fn)(void *, void *), void *ud) { mono_unity_gc_heap_foreach(fn, ud); }
API void il2cpp_stop_gc_world(void) { mono_unity_stop_gc_world(); }
API void il2cpp_start_gc_world(void) { mono_unity_start_gc_world(); }
API void *il2cpp_gc_alloc_fixed(size_t size) { return mono_gc_alloc_fixed_no_descriptor(size, 0 /* MONO_ROOT_SOURCE_EXTERNAL */, NULL, "il2cpp_gc_alloc_fixed"); }
API void il2cpp_gc_free_fixed(void *p) { mono_gc_free_fixed(p); }

// Unity's Mono fork already uses IL2CPP's handle representation (a pointer to the slot, bit 0 set for
// weak handles), which the engine inlines, so the v2 API maps one to one. The legacy uint32 API truncates.
API void *il2cpp_gchandle_new(MonoObject *o, bool pinned) { return mono_gchandle_new_v2(o, pinned); }
API void *il2cpp_gchandle_new_weakref(MonoObject *o, bool resurrect) { return mono_gchandle_new_weakref_v2(o, resurrect); }
API MonoObject *il2cpp_gchandle_get_target(void *h) { return h ? mono_gchandle_get_target_v2(h) : NULL; }
API void il2cpp_gchandle_free(void *h) { if (h) mono_gchandle_free_v2(h); }
API void il2cpp_gchandle_foreach_get_target(void (*fn)(void *, void *), void *ud) { mono_unity_gc_handles_foreach_get_target(fn, ud); }

API uint32_t il2cpp_object_header_size(void) { return mono_unity_object_header_size(); }
API uint32_t il2cpp_array_object_header_size(void) { return mono_unity_array_object_header_size(); }
API uint32_t il2cpp_offset_of_array_length_in_array_object_header(void) { return mono_unity_offset_of_array_length_in_array_object_header(); }
API uint32_t il2cpp_offset_of_array_bounds_in_array_object_header(void) { return mono_unity_offset_of_array_bounds_in_array_object_header(); }
API uint32_t il2cpp_allocation_granularity(void) { return mono_unity_allocation_granularity(); }

API void *il2cpp_unity_liveness_allocate_struct(MonoClass *filter, int max, register_object_callback cb, void *ud, ReallocateArray re) {
    return mono_unity_liveness_allocate_struct(filter, (unsigned)max, cb, ud, re);
}
API void il2cpp_unity_liveness_calculation_from_root(MonoObject *root, void *s) { mono_unity_liveness_calculation_from_root(root, s); }
API void il2cpp_unity_liveness_calculation_from_statics(void *s) { mono_unity_liveness_calculation_from_statics(s); }
API void il2cpp_unity_liveness_finalize(void *s) { mono_unity_liveness_finalize(s); }
API void il2cpp_unity_liveness_free_struct(void *s) { mono_unity_liveness_free_struct(s); }

// ---- methods ----------------------------------------------------------------------------------
API const MonoType *il2cpp_method_get_return_type(const MonoMethod *m) { return mono_signature_get_return_type(mono_method_signature((MonoMethod *)m)); }
API MonoClass *il2cpp_method_get_declaring_type(const MonoMethod *m) { return mono_method_get_class((MonoMethod *)m); }
API MonoClass *il2cpp_method_get_class(const MonoMethod *m) { return mono_method_get_class((MonoMethod *)m); }
API const char *il2cpp_method_get_name(const MonoMethod *m) { if (!m) return NULL; return mono_method_get_name((MonoMethod *)m); }
API const MonoMethod *il2cpp_method_get_from_reflection(const MonoReflectionMethod *rm) {
    // MonoReflectionMethod { MonoObject object; MonoMethod *method; ... }
    return *(MonoMethod *const *)((const char *)rm + sizeof(MonoObject));
}
API MonoReflectionMethod *il2cpp_method_get_object(const MonoMethod *m, MonoClass *refclass) {
    return mono_method_get_object(domain(), (MonoMethod *)m, refclass);
}
API bool il2cpp_method_is_generic(const MonoMethod *m) {
    // Generic method definitions are flagged in the GenericParam table; inflated instances are not definitions.
    MonoMethodSignature *sig = mono_method_signature((MonoMethod *)m);
    (void)sig;
    char *desc = mono_method_full_name((MonoMethod *)m, false);
    bool generic = desc && strchr(desc, '<') && !unity_mono_method_is_inflated((MonoMethod *)m) && strstr(desc, "<T") != NULL;
    mono_free(desc);
    return generic;
}
API bool il2cpp_method_is_inflated(const MonoMethod *m) { return unity_mono_method_is_inflated((MonoMethod *)m); }
API bool il2cpp_method_is_instance(const MonoMethod *m) { return (mono_method_get_flags((MonoMethod *)m, NULL) & 0x10 /* METHOD_ATTRIBUTE_STATIC */) == 0; }
API uint32_t il2cpp_method_get_param_count(const MonoMethod *m) { if (!m) return 0; return mono_signature_get_param_count(mono_method_signature((MonoMethod *)m)); }
API const MonoType *il2cpp_method_get_param(const MonoMethod *m, uint32_t index) {
    MonoMethodSignature *sig = mono_method_signature((MonoMethod *)m);
    void *it = NULL; MonoType *t; uint32_t i = 0;
    while ((t = mono_signature_get_params(sig, &it))) { if (i++ == index) return t; }
    return NULL;
}
API bool il2cpp_method_has_attribute(const MonoMethod *m, MonoClass *attr) {
    MonoCustomAttrInfo *il2cpp_custom_attrs_from_method(const MonoMethod *m);
    MonoCustomAttrInfo *ai = il2cpp_custom_attrs_from_method(m);
    return ai && mono_custom_attrs_has_attr(ai, attr);
}
API uint32_t il2cpp_method_get_flags(const MonoMethod *m, uint32_t *iflags) { return mono_method_get_flags((MonoMethod *)m, iflags); }
API uint32_t il2cpp_method_get_token(const MonoMethod *m) { return mono_method_get_token((MonoMethod *)m); }
API const char *il2cpp_method_get_param_name(const MonoMethod *m, uint32_t index) {
    uint32_t n = il2cpp_method_get_param_count(m);
    if (index >= n) return NULL;
    const char **names = calloc(n, sizeof(char *));
    mono_method_get_param_names((MonoMethod *)m, names);
    const char *r = names[index];
    free(names);
    return r;
}

// ---- profiler (no-ops; the Mono profiler API differs and is optional) -------------------------
API void il2cpp_profiler_install(void *prof, void *shutdown) { (void)prof; (void)shutdown; }
API void il2cpp_profiler_set_events(int events) { (void)events; }
API void il2cpp_profiler_install_enter_leave(void *e, void *l) { (void)e; (void)l; }
API void il2cpp_profiler_install_allocation(void *cb) { (void)cb; }
API void il2cpp_profiler_install_gc(void *cb, void *resize) { (void)cb; (void)resize; }
API void il2cpp_profiler_install_fileio(void *cb) { (void)cb; }
API void il2cpp_profiler_install_thread(void *s, void *e) { (void)s; (void)e; }

// ---- properties -------------------------------------------------------------------------------
API uint32_t il2cpp_property_get_flags(MonoProperty *p) { return mono_property_get_flags(p); }
API MonoMethod *il2cpp_property_get_get_method(MonoProperty *p) { return mono_property_get_get_method(p); }
API MonoMethod *il2cpp_property_get_set_method(MonoProperty *p) { return mono_property_get_set_method(p); }
API const char *il2cpp_property_get_name(MonoProperty *p) { return mono_property_get_name(p); }
API MonoClass *il2cpp_property_get_parent(MonoProperty *p) { return mono_property_get_parent(p); }

// ---- objects ----------------------------------------------------------------------------------
API MonoClass *il2cpp_object_get_class(MonoObject *o) { return mono_object_get_class(o); }
API uint32_t il2cpp_object_get_size(MonoObject *o) { return mono_object_get_size(o); }
API const MonoMethod *il2cpp_object_get_virtual_method(MonoObject *o, const MonoMethod *m) { return mono_object_get_virtual_method(o, (MonoMethod *)m); }
API MonoObject *il2cpp_object_new(const MonoClass *k) { return mono_object_new(domain(), (MonoClass *)k); }
API void *il2cpp_object_unbox(MonoObject *o) { return mono_object_unbox(o); }
API MonoObject *il2cpp_value_box(MonoClass *k, void *data) { return mono_value_box(domain(), k, data); }

// Monitor operations beyond enter/exit go through System.Threading.Monitor.
static void monitor_call(const char *name, int argc, void **args, MonoObject **ret) {
    static MonoClass *monitor;
    if (!monitor) monitor = mono_class_from_name(mono_get_corlib(), "System.Threading", "Monitor");
    MonoMethod *m = mono_class_get_method_from_name(monitor, name, argc);
    MonoObject *exc = NULL;
    MonoObject *r = mono_runtime_invoke(m, NULL, args, &exc);
    if (ret) *ret = r;
}
API void il2cpp_monitor_enter(MonoObject *o) { mono_monitor_enter(o); }
API bool il2cpp_monitor_try_enter(MonoObject *o, uint32_t timeout) { return mono_monitor_try_enter(o, timeout); }
API void il2cpp_monitor_exit(MonoObject *o) { mono_monitor_exit(o); }
API void il2cpp_monitor_pulse(MonoObject *o) { void *a[] = { o }; monitor_call("Pulse", 1, a, NULL); }
API void il2cpp_monitor_pulse_all(MonoObject *o) { void *a[] = { o }; monitor_call("PulseAll", 1, a, NULL); }
API void il2cpp_monitor_wait(MonoObject *o) { void *a[] = { o }; monitor_call("Wait", 1, a, NULL); }
API bool il2cpp_monitor_try_wait(MonoObject *o, uint32_t timeout) {
    int32_t t = (int32_t)timeout; void *a[] = { o, &t }; MonoObject *r = NULL;
    monitor_call("Wait", 2, a, &r);
    return r && *(mono_bool *)mono_object_unbox(r);
}

// ---- runtime ----------------------------------------------------------------------------------
API MonoObject *il2cpp_runtime_invoke(const MonoMethod *m, void *obj, void **params, MonoObject **exc) {
    if (!m) { if (exc) *exc = NULL; LOG("runtime_invoke with NULL method"); return NULL; }
    return mono_runtime_invoke((MonoMethod *)m, obj, params, exc);
}
API MonoObject *il2cpp_runtime_invoke_convert_args(const MonoMethod *m, void *obj, MonoObject **params, int n, MonoObject **exc) {
    MonoArray *arr = mono_array_new(domain(), mono_get_object_class(), n);
    for (int i = 0; i < n; i++) mono_gc_wbarrier_set_arrayref(arr, mono_array_addr_with_size(arr, sizeof(MonoObject *), i), params[i]);
    return mono_runtime_invoke_array((MonoMethod *)m, obj, arr, exc);
}
API void il2cpp_runtime_class_init(MonoClass *k) { mono_runtime_class_init(mono_class_vtable(domain(), k)); }
API void il2cpp_runtime_object_init(MonoObject *o) { mono_runtime_object_init(o); }
API void il2cpp_runtime_object_init_exception(MonoObject *o, MonoObject **exc) {
    MonoMethod *ctor = NULL;
    for (MonoClass *k = mono_object_get_class(o); k && !ctor; k = NULL) ctor = mono_class_get_method_from_name(k, ".ctor", 0);
    if (exc) *exc = NULL;
    if (ctor) mono_runtime_invoke(ctor, o, NULL, exc);
}
API void il2cpp_runtime_unhandled_exception_policy_set(int policy) { mono_runtime_unhandled_exception_policy_set(policy); }

// ---- strings ----------------------------------------------------------------------------------
API int32_t il2cpp_string_length(MonoString *s) { return mono_string_length(s); }
API Il2CppChar *il2cpp_string_chars(MonoString *s) { return mono_string_chars(s); }
API MonoString *il2cpp_string_new(const char *s) { return mono_string_new(domain(), s); }
API MonoString *il2cpp_string_new_len(const char *s, uint32_t len) { return mono_string_new_len(domain(), s, len); }
API MonoString *il2cpp_string_new_utf16(const Il2CppChar *s, int32_t len) { return mono_string_new_utf16(domain(), s, len); }
API MonoString *il2cpp_string_new_wrapper(const char *s) { return mono_string_new_wrapper(s); }
API MonoString *il2cpp_string_intern(MonoString *s) { return mono_string_intern(s); }
API MonoString *il2cpp_string_is_interned(MonoString *s) { return mono_string_is_interned(s); }

// ---- threads ----------------------------------------------------------------------------------
// IL2CPP answers NULL on threads the VM has never seen; Mono asserts. mono_domain_get is NULL there.
static bool thread_attached(void) { return mono_domain_get() != NULL; }
API MonoThread *il2cpp_thread_current(void) { return thread_attached() ? mono_thread_current() : NULL; }
API MonoThread *il2cpp_thread_attach(MonoDomain *d) { return mono_thread_attach(d ? d : domain()); }
API void il2cpp_thread_detach(MonoThread *t) { mono_thread_detach(t); }
API bool il2cpp_is_vm_thread(MonoThread *t) { return t && mono_gc_is_finalizer_thread(t); }

// ---- stack walking ----------------------------------------------------------------------------
typedef struct { int target; int index; Il2CppStackFrameInfo *out; bool found; Il2CppFrameWalkFunc fn; void *ud; } WalkState;
static mono_bool walk_cb(MonoMethod *m, int32_t native_off, int32_t il_off, mono_bool managed, void *data) {
    (void)native_off;
    WalkState *s = data;
    if (!managed) return false;
    Il2CppStackFrameInfo info = { m, 0, 0, il_off, NULL };
    if (s->fn) { s->fn(&info, s->ud); return false; }
    if (s->index++ == s->target) { if (s->out) *s->out = info; s->found = true; return true; }
    return false;
}
API void il2cpp_current_thread_walk_frame_stack(Il2CppFrameWalkFunc fn, void *ud) {
    if (!thread_attached()) return;
    WalkState s = { -1, 0, NULL, false, fn, ud };
    mono_stack_walk(walk_cb, &s);
}
API void il2cpp_thread_walk_frame_stack(MonoThread *t, Il2CppFrameWalkFunc fn, void *ud) {
    if (t && t == il2cpp_thread_current()) il2cpp_current_thread_walk_frame_stack(fn, ud);
}
API bool il2cpp_current_thread_get_frame_at(int32_t offset, Il2CppStackFrameInfo *frame) {
    if (!thread_attached()) return false;
    WalkState s = { offset, 0, frame, false, NULL, NULL };
    mono_stack_walk(walk_cb, &s);
    return s.found;
}
API bool il2cpp_current_thread_get_top_frame(Il2CppStackFrameInfo *frame) { return il2cpp_current_thread_get_frame_at(0, frame); }
API bool il2cpp_thread_get_top_frame(MonoThread *t, Il2CppStackFrameInfo *frame) {
    return t && t == il2cpp_thread_current() && il2cpp_current_thread_get_top_frame(frame);
}
API bool il2cpp_thread_get_frame_at(MonoThread *t, int32_t offset, Il2CppStackFrameInfo *frame) {
    return t && t == il2cpp_thread_current() && il2cpp_current_thread_get_frame_at(offset, frame);
}
API int32_t il2cpp_current_thread_get_stack_depth(void) {
    if (!thread_attached()) return 0;
    WalkState s = { INT32_MAX, 0, NULL, false, NULL, NULL };
    mono_stack_walk(walk_cb, &s);
    return s.index;
}
API int32_t il2cpp_thread_get_stack_depth(MonoThread *t) { return t && t == il2cpp_thread_current() ? il2cpp_current_thread_get_stack_depth() : 0; }
API void il2cpp_override_stack_backtrace(void *fn) { (void)fn; }

// ---- types ------------------------------------------------------------------------------------
API MonoObject *il2cpp_type_get_object(const MonoType *t) { return (MonoObject *)mono_type_get_object(domain(), (MonoType *)t); }
API int il2cpp_type_get_type(const MonoType *t) { return mono_type_get_type((MonoType *)t); }
API MonoClass *il2cpp_type_get_class_or_element_class(const MonoType *t) {
    MonoClass *k = mono_class_from_mono_type((MonoType *)t);
    int tt = mono_type_get_type((MonoType *)t);
    if (tt == MONO_TYPE_SZARRAY || tt == MONO_TYPE_ARRAY || tt == MONO_TYPE_PTR) return mono_class_get_element_class(k);
    return k;
}
API char *il2cpp_type_get_name(const MonoType *t) { return mono_type_get_name((MonoType *)t); }
API bool il2cpp_type_is_byref(const MonoType *t) { return mono_type_is_byref((MonoType *)t); }
API uint32_t il2cpp_type_get_attrs(const MonoType *t) {
    // MonoType { union data (8 bytes); unsigned attrs:16; ... }
    return *(const uint16_t *)((const char *)t + sizeof(void *));
}
API bool il2cpp_type_equals(const MonoType *a, const MonoType *b) { return mono_metadata_type_equal((MonoType *)a, (MonoType *)b); }
API char *il2cpp_type_get_assembly_qualified_name(const MonoType *t) { return mono_type_get_name_full((MonoType *)t, MONO_TYPE_NAME_FORMAT_ASSEMBLY_QUALIFIED); }
API char *il2cpp_type_get_reflection_name(const MonoType *t) { return mono_type_get_name_full((MonoType *)t, MONO_TYPE_NAME_FORMAT_REFLECTION); }
API bool il2cpp_type_is_static(const MonoType *t) { return mono_unity_type_is_static((MonoType *)t); }
API bool il2cpp_type_is_pointer_type(const MonoType *t) { return mono_unity_type_is_pointer_type((MonoType *)t); }

// ---- memory snapshot, plugins, logging, debugger ----------------------------------------------
API void *il2cpp_capture_memory_snapshot(void) { return NULL; }
API void il2cpp_free_captured_memory_snapshot(void *s) { (void)s; }
API void il2cpp_set_find_plugin_callback(const char *(*cb)(const char *)) { mono_set_find_plugin_callback(cb); }
API void il2cpp_register_log_callback(void (*cb)(const char *)) { (void)cb; }
API void il2cpp_debugger_set_agent_options(const char *o) { (void)o; }
API bool il2cpp_is_debugger_attached(void) { return false; }
API void il2cpp_register_debugger_agent_transport(void *t) { (void)t; }
API void il2cpp_debug_foreach_method(void (*fn)(const MonoMethod *, Il2CppMethodDebugInfo *, void *), void *ud) { (void)fn; (void)ud; }
API bool il2cpp_debug_get_method_info(const MonoMethod *m, Il2CppMethodDebugInfo *info) { (void)m; (void)info; return false; }
API void il2cpp_unity_install_unitytls_interface(const void *s) { mono_unity_install_unitytls_interface(s); }

// ---- custom attributes ------------------------------------------------------------------------
// IL2CPP attribute infos live in metadata forever and il2cpp_custom_attrs_free is a no-op; the engine
// relies on that (it keeps infos after "freeing" them). Mono allocates a fresh info per query, so the
// shim caches one per class/method/field and never frees it.
typedef struct AttrEntry { const void *key; MonoCustomAttrInfo *info; struct AttrEntry *next; } AttrEntry;
#define ATTR_BUCKETS 8192
static AttrEntry *g_attr_cache[ATTR_BUCKETS];
static pthread_mutex_t g_attr_lock = PTHREAD_MUTEX_INITIALIZER;
static MonoCustomAttrInfo *cached_attrs(const void *key, MonoCustomAttrInfo *(*load)(const void *)) {
    size_t b = ((uintptr_t)key >> 4) % ATTR_BUCKETS;
    pthread_mutex_lock(&g_attr_lock);
    for (AttrEntry *e = g_attr_cache[b]; e; e = e->next)
        if (e->key == key) { pthread_mutex_unlock(&g_attr_lock); return e->info; }
    pthread_mutex_unlock(&g_attr_lock);
    MonoCustomAttrInfo *info = load(key);
    pthread_mutex_lock(&g_attr_lock);
    AttrEntry *e = malloc(sizeof(AttrEntry));
    e->key = key; e->info = info; e->next = g_attr_cache[b]; g_attr_cache[b] = e;
    pthread_mutex_unlock(&g_attr_lock);
    return info;
}
static MonoCustomAttrInfo *load_class_attrs(const void *k) { return mono_custom_attrs_from_class((MonoClass *)k); }
static MonoCustomAttrInfo *load_method_attrs(const void *m) { return mono_custom_attrs_from_method((MonoMethod *)m); }
static MonoCustomAttrInfo *load_field_attrs(const void *f) {
    return mono_custom_attrs_from_field(mono_field_get_parent((MonoClassField *)f), (MonoClassField *)f);
}
API MonoCustomAttrInfo *il2cpp_custom_attrs_from_class(MonoClass *k) { return cached_attrs(k, load_class_attrs); }
API MonoCustomAttrInfo *il2cpp_custom_attrs_from_method(const MonoMethod *m) { return cached_attrs(m, load_method_attrs); }
API MonoCustomAttrInfo *il2cpp_custom_attrs_from_field(const MonoClassField *f) { return cached_attrs(f, load_field_attrs); }
API MonoObject *il2cpp_custom_attrs_get_attr(MonoCustomAttrInfo *ai, MonoClass *k) { return ai ? mono_custom_attrs_get_attr(ai, k) : NULL; }
API bool il2cpp_custom_attrs_has_attr(MonoCustomAttrInfo *ai, MonoClass *k) { return ai && mono_custom_attrs_has_attr(ai, k); }
API MonoArray *il2cpp_custom_attrs_construct(MonoCustomAttrInfo *ai) {
    return ai ? mono_custom_attrs_construct(ai) : mono_array_new(domain(), mono_get_object_class(), 0);
}
API void il2cpp_custom_attrs_free(MonoCustomAttrInfo *ai) { (void)ai; }

API void il2cpp_set_default_thread_affinity(int64_t mask) { (void)mask; }
API void il2cpp_unity_set_android_network_up_state_func(void *fn) { (void)fn; }
