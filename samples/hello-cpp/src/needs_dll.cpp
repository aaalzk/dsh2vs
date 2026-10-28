#include <cstdio>

namespace payload
{
    int magic();
}

// Fixture: this exe cannot start unless payload.dll sits next to it.
// Copy the exe alone into an empty folder to reproduce STATUS_DLL_NOT_FOUND (0xC0000135).
int main()
{
    std::printf("payload::magic() = %d\n", payload::magic());
    return 0;
}
