#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p .tools
if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks | grep -q '^8\.'; then
  echo "Using installed .NET 8 SDK"
else
  echo "Installing .NET 8 SDK into .tools/dotnet (no administrator privileges)"
  curl --fail --silent --show-error --location https://dot.net/v1/dotnet-install.sh -o .tools/dotnet-install.sh
  bash .tools/dotnet-install.sh --channel 8.0 --install-dir "$PWD/.tools/dotnet"
  export DOTNET_ROOT="$PWD/.tools/dotnet"
  export PATH="$DOTNET_ROOT:$PATH"
fi
dotnet --info
python3 -m venv .tools/requirements-venv
.tools/requirements-venv/bin/python -m pip install --upgrade pip
.tools/requirements-venv/bin/python -m pip install strictdoc
python3 scripts/check_requirements.py
.tools/requirements-venv/bin/strictdoc export requirements --output-dir build/requirements
for project in src/*/*.csproj tests/*/*.csproj examples/*/*.csproj tools/*/*.csproj; do
  if [[ -f "$project" ]]; then dotnet restore "$project"; fi
done
echo "Setup complete. If .NET was installed locally, run: export PATH=\"$PWD/.tools/dotnet:\$PATH\""
