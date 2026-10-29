"""Check the in-process Steam provider boundary without credentials or network."""
import pathlib
import subprocess
import tempfile
import unittest
ROOT = pathlib.Path(__file__).resolve().parents[1]
class SteamAdapterTests(unittest.TestCase):
    def test_identity_ticket_result_and_cancellation(self):
        harness = r'''
#include <assert.h>
#include <stdio.h>
#include "RUNTIME"
static uint32_t requested, cancelled;
static void provider(uint32_t handle, const char *identity, bool cancel) {
    if(cancel) cancelled=handle;
    else { requested=handle; assert(strcmp(identity,"game-service")==0); }
}
int main(void) {
    bpp_steam_set_provider(provider);
    assert(SteamAPI_ISteamUser_GetAuthTicketForWebApi(NULL,"game-service")==0);
    bpp_steam_identity(42,1234);
    assert(SteamAPI_ISteamUser_GetSteamID(NULL)==1234);
    assert(SteamAPI_ISteamUtils_GetAppID(NULL)==42);
    uint32_t h=SteamAPI_ISteamUser_GetAuthTicketForWebApi(NULL,"game-service");
    assert(h && requested==h);
    const uint8_t ticket[]={1,2,3};
    bpp_steam_ticket(42,h,ticket,3,1);
    CallbackMsg msg;
    assert(SteamAPI_ManualDispatch_GetNextCallback(1,&msg));
    TicketForWebApiResponse *response=msg.param;
    assert(msg.callback==168 && response->result==1 && response->ticket_size==3);
    assert(memcmp(response->ticket,ticket,3)==0);
    SteamAPI_ManualDispatch_FreeLastCallback(1);
    bpp_steam_ticket(99,h,ticket,3,1);
    assert(SteamAPI_ManualDispatch_GetNextCallback(1,&msg));
    response=msg.param; assert(response->result!=1 && response->ticket_size==0);
    SteamAPI_ManualDispatch_FreeLastCallback(1);
    SteamAPI_ISteamUser_CancelAuthTicket(NULL,h); assert(cancelled==h);
    bpp_steam_identity(42,0);
    assert(!SteamAPI_ISteamUser_BLoggedOn(NULL));
    assert(SteamAPI_ISteamUser_GetAuthTicketForWebApi(NULL,"game-service")==0);
    return 0;
}
'''.replace('RUNTIME', str(ROOT / 'middleware/steamworks/steam_api_runtime.c'))
        with tempfile.TemporaryDirectory() as folder:
            source = pathlib.Path(folder) / 'test.c'
            source.write_text(harness)
            binary = pathlib.Path(folder) / 'test'
            subprocess.run(['cc','-Wall','-Werror','-pthread',str(source),'-o',str(binary)],check=True,capture_output=True)
            subprocess.run([str(binary)],check=True,timeout=5)
