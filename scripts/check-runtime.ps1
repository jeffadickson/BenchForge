# Pure validation for optional native/standalone Make targets; never builds or queries instruments.
param([string]$RuntimeIdentifier='', [ValidateSet('auto','x64','x86','arm64','arm')][string]$Architecture='auto', [ValidateSet('Debug','Release')][string]$Configuration='Release', [switch]$RequireExecution)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'check-build.ps1')
if ([string]::IsNullOrEmpty($RuntimeIdentifier)) { throw 'Set an explicit runtime identifier for optional native and standalone tools' }
$runtimeHost=Get-CheckHostIdentity
$plan=Get-CheckRequestedPlan -Platform $runtimeHost.platform -HostArchitecture $runtimeHost.architecture -Architecture $Architecture -Configuration $Configuration -RuntimeIdentifier $RuntimeIdentifier -RequireExecution:$RequireExecution
$plan | ConvertTo-Json -Compress
