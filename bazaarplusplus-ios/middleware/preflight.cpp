// Small native checks run identically in Simulator and on a signed device.
#include <sqlite3.h>
#include <stdio.h>
#include <string.h>
#include <unistd.h>
// The upstream public header omits these declarations outside its own PCH.
using abcChrono = double;
class aiCurves;
class aiSubDSample;
#include "abci.h"
extern "C" bool bpp_decompress_native_texture(int,int,int,int,const unsigned char*,int,int,unsigned char*);
extern "C" int bpp_fmod_selftest();
static bool sqlite_probe() {
    sqlite3 *db=nullptr,*copy=nullptr; sqlite3_stmt *query=nullptr; bool ok=false;
    if(sqlite3_open(":memory:",&db)!=SQLITE_OK) goto done;
    if(sqlite3_exec(db,"CREATE TABLE probe(value TEXT); INSERT INTO probe VALUES('native-roundtrip');",nullptr,nullptr,nullptr)!=SQLITE_OK) goto done;
    if(sqlite3_open(":memory:",&copy)!=SQLITE_OK) goto done;
    {
        sqlite3_int64 size=0; auto *bytes=sqlite3_serialize(db,"main",&size,0);
        if(!bytes || size<=0) { sqlite3_free(bytes); goto done; }
        int result=sqlite3_deserialize(copy,"main",bytes,size,size,SQLITE_DESERIALIZE_FREEONCLOSE);
        if(result!=SQLITE_OK) goto done;
    }
    if(sqlite3_prepare_v2(copy,"SELECT value FROM probe",-1,&query,nullptr)!=SQLITE_OK) goto done;
    ok=sqlite3_step(query)==SQLITE_ROW && strcmp((const char*)sqlite3_column_text(query,0),"native-roundtrip")==0;
 done:
    sqlite3_finalize(query);if(copy)sqlite3_close(copy);if(db)sqlite3_close(db);return ok;
}
static bool alembic_probe(const char *path) {
    auto *writer=aeCreateContext(); if(!writer)return false;
    bool opened=aeOpenArchive(writer,path);
    if(opened) { auto *node=aeNewXform(aeGetTopObject(writer),"native-probe"); aeXformData data;data.translation={1,2,3};aeXformWriteSample(node,&data); }
    aeDestroyContext(writer);
    if(!opened)return false;
    auto *reader=aiContextCreate(-140271);bool ok=reader && aiContextLoad(reader,path);
    if(ok) {
        auto *top=aiContextGetTopObject(reader);
        ok=top && aiObjectGetNumChildren(top)==1 && strcmp(aiObjectGetName(aiObjectGetChild(top,0)),"native-probe")==0;
    }
    if(reader)aiContextDestroy(reader);unlink(path);return ok;
}
extern "C" __attribute__((visibility("default"))) int bpp_native_selftest(const char *path) {
    bool sqlite=sqlite_probe(), alembic=alembic_probe(path);
    unsigned char red[8]={0,248,0,0,0,0,0,0},pixels[64]={0};
    bool bc=bpp_decompress_native_texture(97,8,4,4,red,4,4,pixels);
    for(int i=0;i<16;i++)bc=bc && pixels[i*4]==255 && pixels[i*4+1]==0 && pixels[i*4+2]==0 && pixels[i*4+3]==255;
    fprintf(stderr,"NATIVE_PREFLIGHT sqlite=%s alembic=%s bc=%s\n",sqlite?"pass":"fail",alembic?"pass":"fail",bc?"pass":"fail");
    bool audio = bpp_fmod_selftest();
    return sqlite && alembic && bc && audio;
}
