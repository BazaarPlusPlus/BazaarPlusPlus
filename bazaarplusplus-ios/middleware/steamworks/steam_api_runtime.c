// Hand-written half of the steam_api stub: the calls whose results matter to a game. The generated half
// (steam_api_stub.c) returns 0 for everything else.
//
// The App supplies identity and Web API tickets asynchronously through bpp_steam_set_provider.
#include <stdbool.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <pthread.h>

#define API __attribute__((visibility("default"), used))

enum { kGetTicketForWebApiResponse = 168, kMaxTicket = 2560 };

#pragma pack(push, 4)
typedef struct { int32_t user; int32_t callback; void *param; int32_t param_size; } CallbackMsg;
typedef struct { uint32_t ticket_handle; int32_t result; int32_t ticket_size; uint8_t ticket[kMaxTicket]; } TicketForWebApiResponse;
#pragma pack(pop)

typedef struct Pending { int32_t callback; void *param; int32_t size; struct Pending *next; } Pending;
static Pending *g_queue, *g_current;
static pthread_mutex_t g_lock = PTHREAD_MUTEX_INITIALIZER;
static uint32_t g_next_ticket = 1;
static void (*g_provider)(uint32_t, const char *, bool);
static uint64_t g_steam_id;
static uint32_t g_app_id;

API void bpp_steam_set_provider(void (*provider)(uint32_t, const char *, bool)) { g_provider = provider; }
API void bpp_steam_identity(uint32_t app, uint64_t user) {
    __atomic_store_n(&g_app_id, app, __ATOMIC_RELEASE);
    __atomic_store_n(&g_steam_id, user, __ATOMIC_RELEASE);
}

static void enqueue(int32_t callback, void *param, int32_t size) {
    Pending *p = calloc(1, sizeof(*p));
    p->callback = callback; p->param = param; p->size = size;
    pthread_mutex_lock(&g_lock);
    Pending **tail = &g_queue;
    while (*tail) tail = &(*tail)->next;
    *tail = p;
    pthread_mutex_unlock(&g_lock);
}

API uint64_t SteamAPI_ISteamUser_GetSteamID(void *self) { (void)self; return __atomic_load_n(&g_steam_id, __ATOMIC_ACQUIRE); }
API uint32_t SteamAPI_ISteamUtils_GetAppID(void *self) { (void)self; return __atomic_load_n(&g_app_id, __ATOMIC_ACQUIRE); }
API bool SteamAPI_ISteamUser_BLoggedOn(void *self) { return SteamAPI_ISteamUser_GetSteamID(self) != 0; }
API void SteamAPI_ISteamUser_CancelAuthTicket(void *self, uint32_t handle) { (void)self; if (g_provider) g_provider(handle, NULL, true); }

API void bpp_steam_ticket(uint32_t app, uint32_t handle, const uint8_t *ticket, int length, int result) {
    TicketForWebApiResponse *r = calloc(1, sizeof(*r));
    if (!r) return;
    r->ticket_handle = handle;
    r->result = result == 1 && app == __atomic_load_n(&g_app_id, __ATOMIC_ACQUIRE) && __atomic_load_n(&g_steam_id, __ATOMIC_ACQUIRE) && length > 0 && length <= kMaxTicket ? 1 : 2;
    if (r->result == 1) { memcpy(r->ticket, ticket, length); r->ticket_size = length; }
    enqueue(kGetTicketForWebApiResponse, r, sizeof(*r));
}

API uint32_t SteamAPI_ISteamUser_GetAuthTicketForWebApi(void *self, const char *identity) {
    (void)self;
    if (!g_provider || !__atomic_load_n(&g_steam_id, __ATOMIC_ACQUIRE)) return 0;  // k_HAuthTicketInvalid
    uint32_t handle = __atomic_fetch_add(&g_next_ticket, 1, __ATOMIC_RELAXED);
    g_provider(handle, identity, false);
    return handle;
}

API void SteamAPI_ManualDispatch_Init(void) {}
API void SteamAPI_ManualDispatch_RunFrame(int32_t pipe) { (void)pipe; }

API bool SteamAPI_ManualDispatch_GetNextCallback(int32_t pipe, CallbackMsg *out) {
    (void)pipe;
    pthread_mutex_lock(&g_lock);
    Pending *p = g_queue;
    if (p) g_queue = p->next;
    g_current = p;
    pthread_mutex_unlock(&g_lock);
    if (!p) return false;
    out->user = 1; out->callback = p->callback; out->param = p->param; out->param_size = p->size;
    return true;
}

API void SteamAPI_ManualDispatch_FreeLastCallback(int32_t pipe) {
    (void)pipe;
    pthread_mutex_lock(&g_lock);
    Pending *p = g_current; g_current = NULL;
    pthread_mutex_unlock(&g_lock);
    if (p) { free(p->param); free(p); }
}
