# Pure requested-runtime contracts; never builds or runs foreign binaries.
param([string]$Output='build/check-ports/runtime-requests.json')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $root 'scripts/check-build.ps1')
$results=[System.Collections.Generic.List[object]]::new()
function Case([string]$Name,[scriptblock]$Action){ & $Action; $results.Add([pscustomobject]@{case=$Name;result='passed'}) }
function Reject([scriptblock]$Action){ $rejected=$false;try{& $Action | Out-Null}catch{$rejected=$true};if(!$rejected){throw 'Expected rejection'} }
foreach($rid in @('win-x64','win-x86','win-arm64','linux-x64','linux-arm64','linux-arm','osx-x64','osx-arm64')){
    Case "Explicit supported runtime $rid" {
        $parts=$rid.Split('-');$plan=Get-CheckRequestedPlan -Platform $parts[0] -HostArchitecture $parts[1] -RuntimeIdentifier $rid
        if($plan.rid -cne $rid -or !$plan.can_execute -or $plan.architecture -cne $parts[1]){throw 'Incorrect explicit runtime'}
    }
}
Case 'Conflicting explicit architecture' {Reject {Get-CheckRequestedPlan 'win' 'x64' 'x64' 'Release' 'win-arm64'}}
Case 'Cross OS request rejected' {Reject {Get-CheckRequestedPlan 'win' 'x64' 'auto' 'Release' 'linux-x64'}}
foreach($invalid in @('WIN-x64','win-AMD64',"win-x64`n",'../win-x64','linux-x86','osx-arm','win-x64/Debug',' ')){
    Case "Malformed runtime [$invalid]" {Reject {Get-CheckRequestedPlan 'win' 'x64' 'auto' 'Release' $invalid}}
}
Case 'No runtime uses host automatic architecture' {
    $plan=Get-CheckRequestedPlan 'win' 'arm64';if($plan.rid -cne 'win-arm64' -or !$plan.can_execute){throw 'Incorrect automatic plan'}
}
Case 'Foreign architecture may be prepared' {
    $plan=Get-CheckRequestedPlan 'win' 'x64' 'auto' 'Release' 'win-arm64';if($plan.can_execute -or $plan.cache_segment -cne 'win-arm64/Release'){throw 'Foreign plan lost build-only status'}
}
Case 'Foreign architecture cannot execute' {Reject {Get-CheckRequestedPlan 'win' 'x64' 'auto' 'Release' 'win-arm64' -RequireExecution}}
Case 'Matching execution is eligible' {
    $plan=Get-CheckRequestedPlan 'linux' 'x64' 'auto' 'Release' 'linux-x64' -RequireExecution;if(!$plan.can_execute){throw 'Matching process must be eligible'}
}
Case 'Configuration partitions remain separate' {
    $release=Get-CheckRequestedPlan 'win' 'x64' 'auto' 'Release' 'win-x64';$debug=Get-CheckRequestedPlan 'win' 'x64' 'auto' 'Debug' 'win-x64'
    if($release.cache_segment -ceq $debug.cache_segment -or $debug.cache_segment -cne 'win-x64/Debug'){throw 'Configuration collision'}
}
Case 'Noncanonical configuration rejected' {Reject {Get-CheckRequestedPlan 'win' 'x64' 'auto' 'release' 'win-x64'}}
Case 'Unsupported host architecture rejected' {Reject {Get-CheckRequestedPlan 'win' 'mips'}}
Case 'Unsupported explicit architecture rejected' {Reject {Get-CheckRequestedPlan 'win' 'x64' 'mips'}}
Case 'Architecture without RID remains supported' {
    $plan=Get-CheckRequestedPlan 'linux' 'x64' 'arm64';if($plan.rid -cne 'linux-arm64' -or $plan.can_execute){throw 'Incorrect explicit architecture plan'}
}
[System.IO.Directory]::CreateDirectory((Split-Path -Parent ([System.IO.Path]::GetFullPath($Output)))) | Out-Null
[System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($Output),(ConvertTo-Json -InputObject @($results.ToArray()) -Depth 5))
Write-Output "PASS: $($results.Count) requested-runtime contracts"
