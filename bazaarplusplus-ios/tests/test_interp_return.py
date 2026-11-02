"""Execute the interpreter's frame-to-native copy against managed and pinvoke signatures."""
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
MONO = ROOT / 'third_party/mono/mono/mini/interp'


@unittest.skipUnless((MONO / 'interp.c').exists() and shutil.which('cc'), 'requires bootstrapped Mono and a C compiler')
class InterpReturnTests(unittest.TestCase):
    def test_a_managed_return_is_not_copied_at_its_native_size(self):
        source = (MONO / 'interp.c').read_text()
        start = source.index('interp_frame_arg_to_data (MonoInterpFrameHandle')
        function = source[start:source.index('\n}\n', start) + 3]
        program = '''#include <assert.h>
typedef void *gpointer;
typedef void *MonoInterpFrameHandle;
typedef struct { gpointer p; } stackval_data;
typedef struct { stackval_data data; } stackval;
typedef struct { int unused; } InterpMethod;
typedef struct { InterpMethod *imethod; stackval *stack; } InterpFrame;
typedef struct { gpointer ret; gpointer *params; int hasthis; int pinvoke; } MonoMethodSignature;
#define STACK_ADD_BYTES(sp, bytes) ((void)(bytes), (sp))
#define TRUE 1
static int copied_as_pinvoke = -1;
static int stackval_to_data (gpointer type, stackval *val, void *data, int pinvoke) {
    (void)type; (void)val; (void)data;
    copied_as_pinvoke = pinvoke;
    return 8;
}
static int get_arg_offset (InterpMethod *imethod, MonoMethodSignature *sig, int index) {
    (void)imethod; (void)sig; (void)index;
    return 0;
}
static void
''' + function + '''
int main(void) {
    stackval value = {{0}};
    InterpMethod method = {0};
    InterpFrame frame = {&method, &value};
    MonoMethodSignature sig = {0};
    long storage = 0;
    interp_frame_arg_to_data(&frame, &sig, -1, &storage);
    assert(copied_as_pinvoke == 0);
    sig.pinvoke = 1;
    interp_frame_arg_to_data(&frame, &sig, -1, &storage);
    assert(copied_as_pinvoke == 1);
    return 0;
}
'''
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'test.c').write_text(program)
            subprocess.run(['cc', '-std=c11', '-Wall', '-Werror', str(root / 'test.c'), '-o', str(root / 'test')], check=True, capture_output=True)
            subprocess.run([str(root / 'test')], check=True, timeout=5)


if __name__ == '__main__':
    unittest.main()
