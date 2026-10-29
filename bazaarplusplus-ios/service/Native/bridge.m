#import <Foundation/Foundation.h>
#import <Security/Security.h>
#include <mono/jit/jit.h>
#include <mono/metadata/assembly.h>
#include <mono/metadata/threads.h>
#include <mono/metadata/object.h>
#include <unistd.h>
#define API __attribute__((visibility("default"), used))
extern int il2cpp_init(const char *);
extern int bpp_native_selftest(const char *);
extern void bpp_steam_set_provider(void (*request)(uint32_t, const char*, bool));
static MonoClass *service;
static MonoDomain *serviceDomain;

static char *invoke(const char *method, const char *argument) {
    mono_thread_attach(serviceDomain);
    MonoMethod *target = mono_class_get_method_from_name(service, method, argument ? 1 : 0);
    if (!target) return strdup("{\"state\":\"error\",\"message\":\"服务入口不存在\"}");
    MonoString *text = argument ? mono_string_new(serviceDomain, argument) : NULL;
    void *args[] = {text}; MonoObject *exception = NULL;
    MonoObject *result = mono_runtime_invoke(target, NULL, argument ? args : NULL, &exception);
    if (exception) {
        fprintf(stderr, "STEAM_BRIDGE_ERROR %s\n", mono_class_get_name(mono_object_get_class(exception)));
        return strdup("{\"state\":\"error\",\"message\":\"Steam 服务调用失败\"}");
    }
    if (!result || strcmp(method, "Snapshot") != 0) return NULL;
    char *utf8 = mono_string_to_utf8((MonoString *)result);
    char *copy = strdup(utf8); mono_free(utf8); return copy;
}
static void ticketRequest(uint32_t handle, const char *identity, bool cancel) {
    NSDictionary *value = @{ @"handle": @(handle), @"identity": identity ? @(identity) : @"", @"cancel": @(cancel) };
    NSData *json = [NSJSONSerialization dataWithJSONObject:value options:0 error:nil];
    NSString *arg = [[NSString alloc] initWithData:json encoding:NSUTF8StringEncoding];
    char *result = invoke("RequestTicket", arg.UTF8String); free(result);
}
API int bpp_steam_start(const char *config) {
    if (!service) {
        NSString *probe = [NSTemporaryDirectory() stringByAppendingPathComponent:
            [NSString stringWithFormat:@"bpp-native-%@.abc",NSUUID.UUID.UUIDString]];
        if (!bpp_native_selftest(probe.UTF8String)) return 0;
        if (!il2cpp_init("BazaarPlusPlus Services")) return 0;
        serviceDomain = mono_domain_get();
        NSString *path = [NSBundle.mainBundle.bundlePath stringByAppendingPathComponent:@"Data/Managed/BazaarPlusPlus.Ios.Service.dll"];
        MonoAssembly *assembly = mono_domain_assembly_open(serviceDomain, path.UTF8String);
        if (!assembly) return 0;
        service = mono_class_from_name(mono_assembly_get_image(assembly), "BazaarPlusPlus.Ios.Service", "SteamService");
        if (!service) return 0;
    }
    char *error = invoke("Initialize", config);
    if (error) { free(error); return 0; }
    bpp_steam_set_provider(ticketRequest);
    return 1;
}
API void bpp_steam_command(const char *json) { char *error = invoke("Command", json); free(error); }
API char *bpp_steam_snapshot(void) { return invoke("Snapshot", NULL); }
API void bpp_steam_free(void *value) { free(value); }
API int bpp_steam_symlink(const char *target, const char *path) { return symlink(target, path); }
