# Focused architecture and path-safety contracts; no application tests or builds.
param([string]$Output = 'build/check-ports/architecture.json')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $root 'scripts/check-build.ps1')
$results = [System.Collections.Generic.List[object]]::new()
function Case([string]$Name, [scriptblock]$Action) {
    & $Action
    $results.Add([pscustomobject]@{case=$Name;result='passed'})
}
function Reject([scriptblock]$Action) {
    $failed = $false
    try { & $Action | Out-Null } catch { $failed = $true }
    if (!$failed) { throw 'Expected rejection' }
}

foreach ($rid in @('win-x64','win-x86','win-arm64','linux-x64','linux-arm64','linux-arm','osx-x64','osx-arm64')) {
    Case "Supported runtime $rid" {
        $parts = $rid.Split('-')
        $plan = Get-CheckBuildPlan $parts[0] $parts[1] $parts[1] 'Release'
        if ($plan.rid -cne $rid -or !$plan.can_execute -or $plan.cache_segment -cne "$rid/Release") { throw 'Incorrect runtime build identity' }
    }
}
foreach ($platform in @('win','linux','osx')) {
    Case "Auto selects $platform host" {
        $plan = Get-CheckBuildPlan $platform 'arm64' 'auto' 'Debug'
        if ($plan.rid -cne "$platform-arm64" -or $plan.configuration -cne 'Debug') { throw 'Incorrect automatic architecture' }
    }
}
Case 'Cross-architecture build is not executable on caller' {
    $plan = Get-CheckBuildPlan 'win' 'x64' 'arm64'
    if ($plan.can_execute) { throw 'Cross-architecture plan must not execute' }
}
Case 'Unsupported host architecture' { Reject { Get-CheckBuildPlan 'win' 'mips' } }
Case 'Unsupported platform runtime pair' { Reject { Get-CheckBuildPlan 'osx' 'x64' 'x86' } }
Case 'Configuration names are canonical' { Reject { Get-CheckBuildPlan 'win' 'x64' 'auto' 'release' } }
$owned = Join-Path $root 'build/check-ports/owned'
Case 'Parent traversal cannot escape owned cache' { Reject { Resolve-CheckOwnedPath $owned '../outside.cs' } }
Case 'Absolute manifest path is rejected' { Reject { Resolve-CheckOwnedPath $owned ([System.IO.Path]::GetFullPath((Join-Path $root 'outside.cs'))) } }
Case 'Sibling prefix is not containment' { Reject { Resolve-CheckOwnedPath $owned '../owned-other/file.cs' } }
Case 'Empty manifest path is rejected' { Reject { Resolve-CheckOwnedPath $owned '' } }
[System.IO.Directory]::CreateDirectory((Split-Path -Parent ([System.IO.Path]::GetFullPath($Output)))) | Out-Null
[System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($Output),(ConvertTo-Json -InputObject @($results.ToArray()) -Depth 5))
Write-Output "PASS: $($results.Count) architecture and containment contracts"
