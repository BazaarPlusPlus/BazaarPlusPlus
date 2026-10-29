"""Execute Mono's branch resolver against ARM64 branch/call instructions."""
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
MONO = ROOT / 'third_party/mono/mono/mini'


@unittest.skipUnless((MONO / 'aot-runtime.c').exists() and shutil.which('cc'), 'requires bootstrapped Mono and a C compiler')
class AotBranchTests(unittest.TestCase):
    def test_unbox_pool_option_parses_its_full_prefix(self):
        source = (MONO / 'aot-compiler.c').read_text()
        start = source.index('opts->nunbox_arbitrary_trampolines = atoi')
        assignment = source[start:source.index(';', start) + 1]
        program = '''#include <stdlib.h>
#include <string.h>
#include <assert.h>
int main(void) {
    struct { int nunbox_arbitrary_trampolines; } value, *opts = &value;
    const char *arg = "nunbox-arbitrary-trampolines=8192";
''' + assignment + '''
    assert(value.nunbox_arbitrary_trampolines == 8192);
    return 0;
}
'''
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'test.c').write_text(program)
            subprocess.run(['cc', '-Wall', '-Werror', str(root / 'test.c'), '-o', str(root / 'test')], check=True, capture_output=True)
            subprocess.run([str(root / 'test')], check=True, timeout=5)

    def test_preserves_calls_and_terminates_on_self_branches(self):
        source = (MONO / 'aot-runtime.c').read_text()
        resolver = source[source.index('static inline guint8 *\nmethod_address_resolve'):source.index('\n#else\nstatic inline guint8 *\nmethod_address_resolve')]
        source = (MONO / 'tramp-arm64.c').read_text()
        decoder = source[source.index('mono_arch_get_call_target ('):source.index('\nguint32\nmono_arch_get_plt_info_offset')]
        program = '''#include <stdint.h>
#include <stddef.h>
#include <assert.h>
typedef uint8_t guint8;
typedef uint32_t guint32;
typedef int32_t gint32;
#define TARGET_ARM64 1
static guint8 *
''' + decoder + '\n' + resolver + '''
int main(void) {
    guint32 ret[] = {0xd65f03c0};
    assert(method_address_resolve((guint8 *)ret) == (guint8 *)ret);
    guint32 island[] = {0x14000001, 0x14000001, 0xd65f03c0};
    assert(method_address_resolve((guint8 *)island) == (guint8 *)&island[2]);
    guint32 call[] = {0x94000001, 0xd65f03c0};
    assert(method_address_resolve((guint8 *)call) == (guint8 *)call);
    guint32 loop[] = {0x14000000};
    assert(method_address_resolve((guint8 *)loop) == (guint8 *)loop);
    return 0;
}
'''
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'test.c').write_text(program)
            subprocess.run(['cc', '-std=c11', '-Wall', '-Werror', str(root / 'test.c'), '-o', str(root / 'test')], check=True, capture_output=True)
            subprocess.run([str(root / 'test')], check=True, timeout=5)
