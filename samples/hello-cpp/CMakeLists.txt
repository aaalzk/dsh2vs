cmake_minimum_required(VERSION 3.20)
project(hello-cpp CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(math_util STATIC src/math_util.cpp)
target_include_directories(math_util PUBLIC src)

add_executable(hello src/main.cpp)
target_link_libraries(hello PRIVATE math_util)

# Debugging fixture: long enough to attach to, with a loop worth breaking into.
add_executable(loopy src/loopy.cpp)
target_link_libraries(loopy PRIVATE math_util)

# Startup-failure fixture: needs_dll.exe cannot start unless payload.dll sits next to it.
# Copy the exe alone somewhere else to reproduce STATUS_DLL_NOT_FOUND (0xC0000135).
add_library(payload SHARED src/payload.cpp)
# Without exported symbols MSVC produces no import library, and the exe cannot link.
set_target_properties(payload PROPERTIES WINDOWS_EXPORT_ALL_SYMBOLS TRUE)
add_executable(needs_dll src/needs_dll.cpp)
target_link_libraries(needs_dll PRIVATE payload)
