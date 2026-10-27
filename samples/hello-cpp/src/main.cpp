#include <cstdio>

#include "math_util.h"

int main()
{
    const int a = 7;
    const int b = 6;

    std::printf("add(%d, %d) = %d\n", a, b, mathutil::add(a, b));
    std::printf("multiply(%d, %d) = %d\n", a, b, mathutil::multiply(a, b));
    std::printf("divide(%.1f, %.1f) = %.3f\n", 3.0, 4.0, mathutil::divide(3.0, 4.0));

    return 0;
}
