# Primary developer entry point. The existing bootstrap validates and caches the C# host.
param(
    [string]$Command = 'checks',
    [string]$Dotnet = 'dotnet',
    [string]$Python = $(if ($env:OS -eq 'Windows_NT') { 'python' } else { 'python3' }),
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [ValidateSet('auto','x64','x86','arm64','arm')][string]$Architecture = 'auto',
    [string]$RuntimeIdentifier = '',
    [ValidateRange(1,256)][int]$Jobs = 8,
    [ValidateRange(1,3600)][int]$TimeoutSeconds = 300,
    [switch]$Rebuild,
    [switch]$PlanOnly,
    [ValidateSet('build','test','generate')][string]$Task = 'build'
)
$ErrorActionPreference = 'Stop'
$options = @{} + $PSBoundParameters
$options.Python = $Python
& (Join-Path $PSScriptRoot 'scripts/checks.ps1') @options
exit $LASTEXITCODE
