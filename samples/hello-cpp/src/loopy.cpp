#include <windows.h>

#include <cstdio>
#include <cstring>

#include "math_util.h"

namespace
{
    int accumulate(int n)
    {
        int total = 0;
        for (int i = 0; i < n; ++i)
        {
            total += i;
        }

        return total;
    }
}

int main(int argc, char** argv)
{
    const bool waitMode = argc > 1 && std::strcmp(argv[1], "--wait") == 0;
    const bool crashMode = argc > 1 && std::strcmp(argv[1], "--crash") == 0;
    const bool dialogMode = argc > 1 && std::strcmp(argv[1], "--dialog") == 0;

    std::printf("loopy start (pid=%lu, argc=%d)\n",
                static_cast<unsigned long>(GetCurrentProcessId()), argc);
    for (int i = 0; i < argc; ++i)
    {
        std::printf("  argv[%d] = %s\n", i, argv[i]);
    }

    std::fflush(stdout);

    if (crashMode)
    {
        // Fixture for the "unhandled C++ exception / crash dialog" scenario.
        // Under a debugger this stops as a first-chance exception; outside one it raises the
        // Windows Error Reporting dialog that blocks unattended automation.
        std::printf("about to dereference a null pointer...\n");
        std::fflush(stdout);

        volatile int* nullPointer = nullptr;
        *nullPointer = 42;
    }

    if (dialogMode)
    {
        // Fixture for the modal-dialog hazard: the target blocks in a message box, so a plain
        // "continue" never returns. The bridge must be able to interrupt it and show where it is.
        std::printf("showing a blocking message box...\n");
        std::fflush(stdout);

        MessageBoxA(nullptr,
                    "This dialog blocks unattended automation until someone clicks OK.",
                    "dsh2vs fixture",
                    MB_OK | MB_ICONWARNING);
    }

    if (waitMode)
    {
        for (int i = 0; i < 120; ++i)
        {
            Sleep(1000);
        }
    }

    for (int round = 0; round < 3; ++round)
    {
        const int value = accumulate(round + 1);
        std::printf("round %d -> %d (add=%d)\n", round, value, mathutil::add(round, 10));
        std::fflush(stdout);
    }

    std::printf("loopy done\n");
    return 0;
}
