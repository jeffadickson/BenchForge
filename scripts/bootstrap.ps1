param([switch]$Browser)
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

# PowerShell does not throw on a native process's nonzero exit code by default.
function Invoke-Checked([string]$Program, [string[]]$Arguments) {
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Program failed (exit $LASTEXITCODE)." }
}
New-Item -ItemType Directory -Force ".tools" | Out-Null
$python = if (Get-Command py -ErrorAction SilentlyContinue) { "py" } else { "python" }
$pythonArgs = if ($python -eq "py") { @("-3") } else { @() }
Invoke-Checked $python ($pythonArgs + @("-c", "import sys; assert sys.version_info >= (3,10), 'Python 3.10+ required'"))
$sdkVersion = (Get-Content global.json -Raw | ConvertFrom-Json).sdk.version
$localDotnet = Join-Path (Get-Location) ".tools/dotnet"
if (Test-Path (Join-Path $localDotnet "dotnet.exe")) {
    $env:DOTNET_ROOT = $localDotnet
    $env:PATH = "$localDotnet;$env:PATH"
}
$usableSdk = $false
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    & dotnet --version
    $usableSdk = $LASTEXITCODE -eq 0
}
if (-not $usableSdk) {
    Invoke-WebRequest "https://dot.net/v1/dotnet-install.ps1" -OutFile ".tools/dotnet-install.ps1"
    & ".tools/dotnet-install.ps1" -Version $sdkVersion -InstallDir $localDotnet
    if (-not $?) { throw "SDK installation failed." }
    $env:DOTNET_ROOT = $localDotnet
    $env:PATH = "$localDotnet;$env:PATH"
}
Invoke-Checked "dotnet" @("--info")
Invoke-Checked $python ($pythonArgs + @("-m", "venv", ".tools/requirements-venv"))
Invoke-Checked ".tools/requirements-venv/Scripts/python.exe" @("-m", "pip", "install", "-r", "tools/requirements.txt")
Invoke-Checked $python ($pythonArgs + @("scripts/check_requirements.py"))
Invoke-Checked ".tools/requirements-venv/Scripts/strictdoc.exe" @("export", "requirements", "--output-dir", "build/requirements")
Invoke-Checked $python ($pythonArgs + @("scripts/requirements_scoreboard.py"))
Get-ChildItem src,tests,examples,tools -Filter *.csproj -Recurse | ForEach-Object {
    Invoke-Checked "dotnet" @("restore", $_.FullName)
}
if ($Browser) {
    Invoke-Checked "dotnet" @("run", "--project", "tools/BenchDouble.BrowserSetup", "--", "install", "chromium")
}
Write-Host "Setup complete. Run dotnet commands in this session, or use .tools/dotnet/dotnet.exe."
