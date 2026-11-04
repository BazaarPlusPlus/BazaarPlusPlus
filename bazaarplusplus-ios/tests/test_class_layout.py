"""Check the MonoClass bit that Unity's mono_class_setup_interfaces reads by a hard-coded offset."""
from pathlib import Path
import re
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
MONO = ROOT / 'third_party/mono'
BUILDS = sorted((ROOT / 'build').glob('mono-*-arm64-obj/config.h'))
PROGRAM = '''#include <config.h>
#include <glib.h>
#define REALLY_INCLUDE_CLASS_DEF 1
#include <mono/metadata/class-internals.h>
#include <mono/metadata/class-private-definition.h>
#include <stdio.h>
#include <string.h>
int main(void) {
    MonoClass klass;
    memset(&klass, 0, sizeof klass);
    klass.interfaces_inited = 1;
    const unsigned char *base = (const unsigned char *)&klass.min_align;
    for (int byte = 0; byte < 32; byte++)
        for (int bit = 0; bit < 8; bit++)
            if ((base[byte] >> bit) & 1) printf("%d %d\\n", byte, bit);
    return 0;
}
'''


@unittest.skipUnless(BUILDS and (MONO / 'mono/metadata/class-init.c').exists(), 'requires a configured Mono build')
class ClassLayoutTests(unittest.TestCase):
    def expected(self):
        """Read the non-MSVC, no-Remoting offsets from class-init.c; these builds disable Remoting."""
        source = (MONO / 'mono/metadata/class-init.c').read_text()
        block = source[source.index('#if defined _MSC_VER\n#ifndef DISABLE_REMOTING'):]
        block = block[block.index('#else\n#ifndef DISABLE_REMOTING'):]
        block = block[block.index('#else\n', 6):]
        byte = re.search(r'byte_offset_from_min_align = (\d+)', block).group(1)
        bit = re.search(r'bit_offset = (\d+)', block).group(1)
        return f'{byte} {bit}'

    def test_hard_coded_offset_finds_interfaces_inited(self):
        for config in BUILDS:
            with self.subTest(build=config.parent.name):
                self.assertIn('#define DISABLE_REMOTING', config.read_text())
                with tempfile.TemporaryDirectory() as directory:
                    root = Path(directory)
                    (root / 'probe.c').write_text(PROGRAM)
                    build = config.parent
                    subprocess.run(['xcrun', 'clang', '-std=gnu11', '-w', '-Wl,-undefined,dynamic_lookup',
                                    f'-I{build}', f'-I{MONO}', f'-I{MONO}/mono', f'-I{MONO}/mono/eglib', f'-I{build}/mono/eglib',
                                    str(root / 'probe.c'), '-o', str(root / 'probe')], check=True, capture_output=True)
                    found = subprocess.run([str(root / 'probe')], check=True, capture_output=True, text=True, timeout=5)
                self.assertEqual(found.stdout.strip(), self.expected())
