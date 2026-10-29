import importlib.util, subprocess, tempfile, unittest, hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('device_engine',ROOT/'runtime/engine/prepare-device.py');engine=importlib.util.module_from_spec(spec);spec.loader.exec_module(engine)
ARCHIVE=ROOT/'runtime/engine/shell/Builds/iOSDevice/Libraries/libiPhone-lib.a'
@unittest.skipUnless(ARCHIVE.exists(),'requires pinned Unity device export')
class DeviceEngineTests(unittest.TestCase):
    def test_patch_is_local_and_fails_on_changed_instruction(self):
        data=subprocess.check_output(['xcrun','ar','-p',str(ARCHIVE),'Runtime_Serialize_1_d4es8.o']);obj=engine.ObjectFile(data);offset=obj.patch_target()
        self.assertEqual(data[:offset],obj.data[:offset]);self.assertEqual(data[offset+4:],obj.data[offset+4:]);self.assertNotEqual(data[offset:offset+4],obj.data[offset:offset+4])
        with self.assertRaises(ValueError):obj.patch_target()
    def test_both_overloads_are_renamed_before_linking(self):
        data=subprocess.check_output(['xcrun','ar','-p',str(ARCHIVE),'Runtime_Graphics_8_d4es8.o']);obj=engine.ObjectFile(data)
        for old,new in [(engine.DECOMPRESS,'_bpp_engine_decompress_original'),(engine.DECOMPRESS_TEXTURE,'_bpp_engine_decompress_texture_original')]:obj.rename_definition(old,new)
        parsed=engine.ObjectFile(bytes(obj.data))
        self.assertNotIn(engine.DECOMPRESS,parsed.symbols);self.assertIn('_bpp_engine_decompress_original',parsed.symbols)
        self.assertNotIn(engine.DECOMPRESS_TEXTURE,parsed.symbols);self.assertIn('_bpp_engine_decompress_texture_original',parsed.symbols)
    def test_link_wrapper_routes_bc_and_retains_other_formats(self):
        source='''#include <cassert>
#include "'''+str(ROOT/'runtime/engine/device_texture.cpp')+'''"
int originalCalls, decoderCalls, sourceFormat, destFormat;
GraphicsFormat GetLinearFormat(GraphicsFormat f) { return (GraphicsFormat)(f==100?101:f); }
GraphicsFormat GetGraphicsFormat_Native_TextureFormat(TextureFormat, bool) { return (GraphicsFormat)100; }
extern "C" bool bpp_engine_decompress_original(GraphicsFormat,GraphicsFormat,int,int,const uint32_t*,int,int,uint32_t*) { ++originalCalls;return true; }
extern "C" bool bpp_decompress_native_texture(int s,int d,int,int,const uint8_t*,int,int,uint8_t*) { ++decoderCalls;sourceFormat=s;destFormat=d;return false; }
int main() {
 assert(DecompressNativeTexture((GraphicsFormat)111,(GraphicsFormat)8,4,4,nullptr,4,4,nullptr));assert(originalCalls==1 && decoderCalls==0);
 assert(!DecompressNativeTexture((GraphicsFormat)100,(GraphicsFormat)8,4,4,nullptr,4,4,nullptr));assert(originalCalls==1 && decoderCalls==1 && sourceFormat==101 && destFormat==8);
 assert(!DecompressNativeTexture((TextureFormat)10,(TextureFormat)10,4,4,nullptr,4,4,nullptr));assert(decoderCalls==2 && sourceFormat==101);
}
'''
        with tempfile.TemporaryDirectory() as tmp:
            p=Path(tmp);(p/'test.cpp').write_text(source);subprocess.run(['xcrun','clang++','-std=c++14',str(p/'test.cpp'),'-o',str(p/'test')],check=True,capture_output=True);subprocess.run([str(p/'test')],check=True)
    def test_wrong_engine_is_rejected_before_output(self):
        with tempfile.TemporaryDirectory() as tmp:
            p=Path(tmp);(p/'wrong.a').write_bytes(b'not the pinned engine')
            with self.assertRaises(ValueError):engine.prepare(p/'wrong.a',p/'output')
            self.assertFalse((p/'output').exists())
