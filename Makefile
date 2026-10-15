.PHONY: generate build test check-generated
DOTNET ?= dotnet
generate:
	$(DOTNET) run --project tools/BenchDouble.DriverGen/BenchDouble.DriverGen.csproj -- instrument-definitions src/BenchDouble/Generated

build: generate
	$(DOTNET) build src/BenchDouble/BenchDouble.csproj

test: generate
	$(DOTNET) test tests/BenchDouble.Tests/BenchDouble.Tests.csproj

check-generated: generate
	git diff --exit-code -- src/BenchDouble/Generated
