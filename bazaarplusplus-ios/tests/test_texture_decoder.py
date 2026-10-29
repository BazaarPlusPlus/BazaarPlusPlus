"""Exercise the decoder shared by the Simulator hook and the device link wrapper."""
import ctypes as C
from pathlib import Path
import shutil, subprocess, tempfile, unittest
ROOT=Path(__file__).resolve().parents[1]
@unittest.skipUnless(shutil.which('xcrun'),'requires Apple clang and dispatch')
class TextureDecoderTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp=tempfile.TemporaryDirectory();p=Path(cls.tmp.name)
        (p/'log.c').write_text('void bpp_log(const char *fmt, ...) {}\n')
        subprocess.run(['xcrun','clang','-dynamiclib','-fblocks','-DBPP_TEXTURE_DECODER_ONLY',str(ROOT/'runtime/bridge/engine_hooks.c'),str(p/'log.c'),'-o',str(p/'decoder.dylib')],check=True,capture_output=True)
        cls.lib=C.CDLL(str(p/'decoder.dylib'));cls.decode=cls.lib.bpp_decompress_native_texture
        cls.decode.argtypes=[C.c_int,C.c_int,C.c_int,C.c_int,C.c_void_p,C.c_int,C.c_int,C.c_void_p];cls.decode.restype=C.c_bool
    @classmethod
    def tearDownClass(cls): cls.tmp.cleanup()
    def image(self,fmt,block,width=4,height=4,dst=8,size=4):
        src=C.create_string_buffer(block);out=(C.c_ubyte*(width*height*size+32))(*([0xa5]*(width*height*size+32)))
        self.assertTrue(self.decode(fmt,dst,width,height,src,width,height,out));self.assertEqual(bytes(out)[-32:],b'\xa5'*32)
        return bytes(out)[:-32]
    def test_bc1_bc2_bc3_and_partial_blocks(self):
        red=b'\x00\xf8\x00\x00'+b'\x00'*4
        for fmt,block in [(97,red),(99,b'\xff'*8+red),(101,b'\xff\x00'+b'\x00'*6+red)]:
            self.assertEqual(self.image(fmt,block),bytes([255,0,0,255])*16)
            self.assertEqual(self.image(fmt,block*2,5,3),bytes([255,0,0,255])*15)
    def test_signed_channels_preserve_negative_values(self):
        import struct
        neg=b'\x81\x7f'+b'\0'*6;pos=b'\x7f\x81'+b'\0'*6
        self.assertEqual(struct.unpack('<4f',self.image(103,neg,dst=52,size=16)[:16]),(-1,0,0,1))
        self.assertEqual(struct.unpack('<4f',self.image(105,neg+pos,dst=52,size=16)[:16]),(-1,1,0,1))
        self.assertEqual(self.image(103,neg,dst=5,size=1),b'\0'*16)
    def test_bc6_signed_enum_and_bc7(self):
        import struct
        block=bytes([3,255,23,45,67,89,123,201,44,56,78,99,121,145,167,255]);src=C.create_string_buffer(block)
        reference=(C.c_float*48)();fn=self.lib.bcdec_bc6h_float;fn.argtypes=[C.c_void_p,C.c_void_p,C.c_int,C.c_int]
        for fmt,signed in [(106,0),(107,1)]:
            fn(src,reference,12,signed);actual=struct.unpack('<64f',self.image(fmt,block,dst=52,size=16))
            for i in range(16):self.assertEqual(actual[i*4:i*4+3],tuple(reference[i*3:i*3+3]))
        self.assertEqual(self.image(109,b'\x40'+b'\0'*15),b'\0'*64)
    def test_parallel_decode_and_invalid_dimensions(self):
        red=b'\x00\xf8\x00\x00'+b'\0'*4
        self.assertEqual(self.image(97,red*4096,256,256),bytes([255,0,0,255])*65536)
        src=C.create_string_buffer(red);dst=C.create_string_buffer(64)
        for w,h in [(0,4),(-1,4),(4,0),(2147483647,4)]:self.assertFalse(self.decode(97,8,w,h,src,4,4,dst))
        self.assertFalse(self.decode(999,8,4,4,src,4,4,dst))
