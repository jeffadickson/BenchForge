cmake_minimum_required(VERSION 3.20)
project(BenchForgeFastAudit LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

find_package(Threads REQUIRED)

add_executable(benchforge-fast-audit fast_audit.cpp)
target_link_libraries(benchforge-fast-audit PRIVATE Threads::Threads)
if(MSVC)
  target_compile_options(benchforge-fast-audit PRIVATE /O2 /W4)
else()
  target_compile_options(benchforge-fast-audit PRIVATE -O3 -Wall -Wextra -Wpedantic)
endif()
