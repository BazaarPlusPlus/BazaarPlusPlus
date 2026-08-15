#include <dlfcn.h>
#include <stdio.h>

int main(int argc, char **argv)
{
    if (argc < 3)
    {
        fprintf(stderr, "usage: %s <mach-o> <symbol> [symbol...]\n", argv[0]);
        return 2;
    }

    void *image = dlopen(argv[1], RTLD_NOW | RTLD_LOCAL);
    if (image == NULL)
    {
        fprintf(stderr, "dlopen failed: %s\n", dlerror());
        return 1;
    }

    for (int index = 2; index < argc; index++)
    {
        dlerror();
        if (dlsym(image, argv[index]) == NULL)
        {
            const char *error = dlerror();
            fprintf(stderr, "dlsym failed for %s: %s\n", argv[index], error == NULL ? "unknown" : error);
            dlclose(image);
            return 1;
        }
    }

    if (dlclose(image) != 0)
    {
        fprintf(stderr, "dlclose failed: %s\n", dlerror());
        return 1;
    }
    return 0;
}
