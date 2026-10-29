"""Check native decoding against SteamKit's public encrypted Zstd test vector.
Vector: SteamRE/SteamKit commit 24131c045655ddaa1e0e60880b2b1af28f69e89c,
SteamKit2/Tests/Files/depot_3441461_chunk_9e72678e305540630a665b93e1463bc3983eb55a.bin (LGPL-2.1).
"""
import base64
import ctypes
import hashlib
import json
import pathlib
import subprocess
import sys
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
VECTOR = base64.b64decode("1cuaYsN8qoj9pAR0RiNfC/08IEoQjpfdt7iOehqigwcIKEfxoE/DWIDzJTk0fPkH8KCEWKkfH/Tg3On57AH97dop/9FanPOgz6tSbttNYNz2iRDEIobLJuntPpXWzqsYA1RbTkWsgVC05YUha3/H49CMhTkES7mNlCjNrhzFjualudqf5955p++Y6sPVq4To6T0/wWysSREwHKdpYIeoIGkJ9tmxlMnBwU2fGth42uo=")

@unittest.skipUnless(sys.platform == 'darwin', 'CommonCrypto requires macOS')
class SteamChunkTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        version = json.loads((ROOT / 'toolchain.lock.json').read_text())['zstd']['version']
        source = ROOT / ('build/vendor/zstd-' + version + '/lib')
        if not source.is_dir():
            raise unittest.SkipTest('Run service/build.sh to restore pinned zstd')
        cls.folder = tempfile.TemporaryDirectory()
        cls.addClassCleanup(cls.folder.cleanup)
        library = pathlib.Path(cls.folder.name) / 'chunks.dylib'
        subprocess.run(['cc', '-shared', '-O1', '-DZSTD_DISABLE_ASM', '-I'+str(source),
            str(ROOT/'service/Native/chunks.c'), *map(str,source.glob('common/*.c')),
            *map(str,source.glob('decompress/*.c')), '-lz', '-o', str(library)], check=True, capture_output=True)
        cls.lib = ctypes.CDLL(str(library))
        cls.lib.bpp_steam_decrypt.argtypes = [ctypes.c_void_p,ctypes.c_int,ctypes.c_void_p,ctypes.c_void_p]
        cls.lib.bpp_steam_zstd.argtypes = [ctypes.c_void_p,ctypes.c_int,ctypes.c_void_p,ctypes.c_int]
        cls.lib.bpp_steam_file_sha1.argtypes = [ctypes.c_char_p, ctypes.c_void_p]

    def test_file_hash_matches_standard_digest_and_reports_read_errors(self):
        path = pathlib.Path(self.folder.name) / 'hash-input'
        digest = ctypes.create_string_buffer(20)
        for data in (b'', b'abc', bytes(range(256)) * 1025):
            path.write_bytes(data)
            self.assertEqual(self.lib.bpp_steam_file_sha1(bytes(path), digest), 0)
            self.assertEqual(digest.raw, hashlib.sha1(data).digest())
        self.assertEqual(self.lib.bpp_steam_file_sha1(bytes(path.with_name('missing')), digest), -1)
        self.assertEqual(self.lib.bpp_steam_file_sha1(bytes(path.parent), digest), -1)

    def decrypted(self):
        output = ctypes.create_string_buffer(len(VECTOR))
        size = self.lib.bpp_steam_decrypt(VECTOR,len(VECTOR),bytes(range(1,33)),output)
        self.assertGreater(size,0)
        return output.raw[:size]

    def test_public_encrypted_depot_chunk(self):
        data = self.decrypted()
        output = ctypes.create_string_buffer(156)
        self.assertEqual(self.lib.bpp_steam_zstd(data,len(data),output,len(output)),156)
        self.assertEqual(hashlib.sha1(output.raw).hexdigest(),'9e72678e305540630a665b93e1463bc3983eb55a')

    def test_rejects_truncation_crc_and_size_mismatch(self):
        output = ctypes.create_string_buffer(156)
        self.assertLess(self.lib.bpp_steam_decrypt(VECTOR,len(VECTOR)-1,bytes(range(1,33)),output),0)
        data = self.decrypted()
        bad_crc = bytearray(data); bad_crc[4] ^= 1
        for invalid in (data[:-1],bytes(bad_crc),data[:8]):
            self.assertLess(self.lib.bpp_steam_zstd(invalid,len(invalid),output,156),0)
        self.assertLess(self.lib.bpp_steam_zstd(data,len(data),output,155),0)
