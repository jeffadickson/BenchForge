#!/usr/bin/env bash
# Provision the pinned SDK, documentation environment and all project packages.
set -euo pipefail
cd "$(dirname "$0")/.."
browser=false
for option in "$@"; do
  case "$option" in
    --browser) browser=true ;;
    *) echo "Usage: bash scripts/bootstrap.sh [--browser]" >&2; exit 2 ;;
  esac
done
command -v python3 >/dev/null || { echo "Python 3.10+ is required for setup." >&2; exit 1; }
python3 -c 'import sys; assert sys.version_info >= (3,10), "Python 3.10+ required"'
mkdir -p .tools
sdk_version=$(python3 -c 'import json; print(json.load(open("global.json"))["sdk"]["version"])')
if [[ -x "$PWD/.tools/dotnet/dotnet" ]]; then
  export DOTNET_ROOT="$PWD/.tools/dotnet"
  export PATH="$DOTNET_ROOT:$PATH"
fi
if ! command -v dotnet >/dev/null || ! dotnet --version >/dev/null 2>&1; then
  command -v curl >/dev/null || { echo "curl is required to download .NET." >&2; exit 1; }
  echo "Installing SDK $sdk_version into .tools/dotnet"
  curl --fail --silent --show-error --location https://dot.net/v1/dotnet-install.sh -o .tools/dotnet-install.sh
  bash .tools/dotnet-install.sh --version "$sdk_version" --install-dir "$PWD/.tools/dotnet"
  export DOTNET_ROOT="$PWD/.tools/dotnet"
  export PATH="$DOTNET_ROOT:$PATH"
fi
dotnet --info
python3 -m venv .tools/requirements-venv
.tools/requirements-venv/bin/python -m pip install -r tools/requirements.txt
python3 scripts/check_requirements.py
.tools/requirements-venv/bin/strictdoc export requirements --output-dir build/requirements
python3 scripts/requirements_scoreboard.py
# Discover projects so a newly added package is automatically restored.
for project in src/*/*.csproj tests/*/*.csproj examples/*/*.csproj tools/*/*.csproj; do
  if [[ -f "$project" ]]; then dotnet restore "$project"; fi
done
if [[ "$browser" == true ]]; then
  dotnet run --project tools/BenchDouble.BrowserSetup -- install chromium
fi
echo "Setup complete. Make automatically uses .tools/dotnet when present."
echo "For direct dotnet commands: export PATH=\"$PWD/.tools/dotnet:\$PATH\""
