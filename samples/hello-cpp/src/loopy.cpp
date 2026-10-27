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

    std::printf("loopy start (pid=%lu)\n", static_cast<unsigned long>(GetCurrentProcessId()));
    std::fflush(stdout);

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
