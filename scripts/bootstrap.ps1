$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")
New-Item -ItemType Directory -Force ".tools" | Out-Null
$hasNet8 = $false
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
  $hasNet8 = [bool]((dotnet --list-sdks) -match "^8\.")
}
if (-not $hasNet8) {
  Write-Host "Installing .NET 8 SDK locally"
  Invoke-WebRequest "https://dot.net/v1/dotnet-install.ps1" -OutFile ".tools/dotnet-install.ps1"
  & ".tools/dotnet-install.ps1" -Channel 8.0 -InstallDir (Join-Path (Get-Location) ".tools/dotnet")
  $env:DOTNET_ROOT = Join-Path (Get-Location) ".tools/dotnet"
  $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
}
dotnet --info
py -3 -m venv ".tools/requirements-venv"
& ".tools/requirements-venv/Scripts/python.exe" -m pip install --upgrade pip
& ".tools/requirements-venv/Scripts/python.exe" -m pip install strictdoc
py -3 scripts/check_requirements.py
& ".tools/requirements-venv/Scripts/strictdoc.exe" export requirements --output-dir build/requirements
Get-ChildItem src,tests,examples,tools -Filter *.csproj -Recurse | ForEach-Object {
  dotnet restore $_.FullName
  if ($LASTEXITCODE -ne 0) { throw "Restore failed: $($_.FullName)" }
}
Write-Host "Setup complete; local .NET path is available in this PowerShell session."
