# BenchForge.FastAudit (C++20)

Zero-dependency, multithreaded C++20 native helper for high-frequency repeated repository scans where startup and file-scan latency matter (`neutrality` across all tracked Git files, and `paths` verification across requirements CSV matrices).

## Build & Run

```bash
g++ -O3 -std=c++20 -pthread tools/BenchForge.FastAudit/fast_audit.cpp -o .tools/fast-audit/benchforge-fast-audit
.tools/fast-audit/benchforge-fast-audit all --strict
```

Or via CMake on Windows/macOS/Linux:

```bash
cmake -S tools/BenchForge.FastAudit -B build/fast-audit -DCMAKE_BUILD_TYPE=Release
cmake --build build/fast-audit --config Release
```

`BenchForge.Checks` (C# `.NET 11`) remains the primary all-in-one helper CLI, and the original Python scripts remain zero-build reference oracles.
