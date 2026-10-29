// Host the AOT compiler from the same patched Mono configuration as the iOS runtime.
#include <stdarg.h>
#include <stdio.h>
extern int mono_main(int argc, char **argv);
extern void mono_unity_set_vprintf_func(int (*callback)(const char *, va_list));
static int log_message(const char *format, va_list args) { return vfprintf(stderr, format, args); }
int main(int argc, char **argv) {
    mono_unity_set_vprintf_func(log_message);
    return mono_main(argc, argv);
}
