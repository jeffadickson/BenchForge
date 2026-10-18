param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [string]$Cache = '',
    [string]$Timing = '',
    [string]$Python = 'python',
    [string]$Dotnet = 'dotnet',
    [int]$Jobs = 8,
    [ValidateSet('auto','x64','x86','arm64','arm')][string]$Architecture = 'auto',
    [string]$RuntimeIdentifier = '',
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [ValidateSet('checks','checks-only','requirements','scoreboard','neutrality','neutrality-strict','roslyn','preflight','docs','compare','identity')][string]$Command = 'checks',
    [ValidateRange(1,3600)][int]$TimeoutSeconds = 300,
    [ValidateRange(1,3600)][int]$LockTimeoutSeconds = 300,
    [switch]$PlanOnly,
    [switch]$Rebuild,
    [switch]$PrepareOnly
)
# Bootstrap only: all checks and scheduling live in the shared C# workflow host.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'check-build.ps1')
if ($Jobs -lt 1 -or $Jobs -gt 256) { throw 'Jobs must be 1..256' }
$runtimeHost = Get-CheckHostIdentity
$plan = Get-CheckRequestedPlan -Platform $runtimeHost.platform -HostArchitecture $runtimeHost.architecture -Architecture $Architecture -Configuration $Configuration -RuntimeIdentifier $RuntimeIdentifier
if ($PlanOnly) { $plan | ConvertTo-Json -Compress; exit 0 }
if (!$PrepareOnly -and !$plan.can_execute) { throw 'Cross-architecture builds require -PrepareOnly; use the matching runtime to execute checks' }
$Root = [System.IO.Path]::GetFullPath($Root)
if (!$Cache) { $Cache = Join-Path $Root '.tools/check-workflow' }
$Cache = [System.IO.Path]::GetFullPath((Join-Path $Cache $plan.cache_segment))
[System.IO.Directory]::CreateDirectory($Cache) | Out-Null
# A cache belongs to one build at a time; a failed contender never changes it.
$lock = Get-CheckCacheLock (Join-Path $Cache 'build.lock') ($LockTimeoutSeconds * 1000)
try {
if (!$Timing) { $Timing = Join-Path $Root 'build/check-workflow-timing.json' }
$stages = [System.Collections.Generic.List[object]]::new()

function Invoke-WorkflowStep([string]$Name, [string]$Executable, [string[]]$Arguments) {
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    # Windows PowerShell 5.1 promotes native stderr to error records. Preserve the
    # process exit status instead; unittest legitimately writes success to stderr.
    $savedPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Executable @Arguments | Out-Host; $code = $LASTEXITCODE }
    finally { $ErrorActionPreference = $savedPreference }
    $stages.Add([pscustomobject]@{name=$Name;milliseconds=$watch.Elapsed.TotalMilliseconds;exit_code=$code})
    if ($code -ne 0) { throw "$Name failed with exit code $code" }
}

$inputs = @(
    Get-ChildItem -LiteralPath (Join-Path $Root 'tools/BenchForge.CheckWorkflow') -File | Where-Object { $_.Extension -in '.cs','.csproj' }
    Get-ChildItem -LiteralPath (Join-Path $Root 'tools/BenchForge.Checks') -File | Where-Object { $_.Extension -eq '.cs' -or $_.Name -eq 'scoreboard.html' }
    Get-Item -LiteralPath (Join-Path $Root 'tools/BenchDouble.DocumentationAudit/RoslynAudit.cs')
    Get-Item -LiteralPath (Join-Path $Root 'scripts/python_ast_inventory.py')
    foreach ($name in @('global.json','Directory.Build.props','Directory.Build.targets','Directory.Packages.props','NuGet.Config')) {
        if (Test-Path -LiteralPath (Join-Path $Root $name)) { Get-Item -LiteralPath (Join-Path $Root $name) }
    }
) | Sort-Object FullName
$dotnetPath = (Get-Command -Name $Dotnet -CommandType Application -ErrorAction Stop).Source
$sdk = (& $dotnetPath --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or !$sdk) { throw 'Cannot identify the .NET SDK' }
$sha = [System.Security.Cryptography.SHA256]::Create()
try {
    $fingerprintText = ($plan | ConvertTo-Json -Compress) + "`nSDK:$sdk`nHOST:$dotnetPath`n" + (($inputs | ForEach-Object {
        $stream = [System.IO.File]::OpenRead($_.FullName)
        try { $fileHash = [System.BitConverter]::ToString($sha.ComputeHash($stream)) }
        finally { $stream.Dispose() }
        $_.FullName.Substring($Root.Length) + ':' + $fileHash
    }) -join "`n")
    $fingerprint = [System.BitConverter]::ToString($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($fingerprintText)))
}
finally { $sha.Dispose() }
$fingerprintFile = Join-Path $Cache 'source.sha256'
$dll = Join-Path $Cache 'bin/BenchForge.CheckWorkflow.dll'
$required = @('BenchForge.CheckWorkflow.dll','BenchForge.CheckWorkflow.deps.json','BenchForge.CheckWorkflow.runtimeconfig.json','Microsoft.CodeAnalysis.dll','Microsoft.CodeAnalysis.CSharp.dll','python_ast_inventory.py')
$complete = @($required | Where-Object { !(Test-Path -LiteralPath (Join-Path $Cache ('bin/'+$_))) }).Count -eq 0
$hit = !$Rebuild -and $complete -and (Test-Path -LiteralPath $fingerprintFile) -and ([System.IO.File]::ReadAllText($fingerprintFile) -eq $fingerprint)
$artifactManifest = Join-Path $Cache 'artifacts.json'
if ($hit) {
    try {
        $recorded = [System.IO.File]::ReadAllText($artifactManifest) | ConvertFrom-Json
        foreach ($name in $required) {
            if ($recorded.$name -cne (Get-CheckFileHash (Join-Path $Cache ('bin/'+$name)))) { $hit = $false; break }
        }
    }
    catch { $hit = $false }
}

try {
    if (!$hit) {
        if (Test-Path -LiteralPath $fingerprintFile) { Remove-Item -LiteralPath $fingerprintFile }
        $manifest = Join-Path $Cache 'source-files.json'
        $relativeInputs = @($inputs | ForEach-Object { $_.FullName.Substring($Root.Length).TrimStart([char[]]@('\','/')) })
        if (Test-Path -LiteralPath $manifest) {
            $sourceRoot = [System.IO.Path]::GetFullPath((Join-Path $Cache 'source')) + [System.IO.Path]::DirectorySeparatorChar
            foreach ($oldRelative in ([System.IO.File]::ReadAllText($manifest) | ConvertFrom-Json)) {
                if ($oldRelative -notin $relativeInputs) {
                    $oldFile = Resolve-CheckOwnedPath $sourceRoot $oldRelative
                    if (Test-Path -LiteralPath $oldFile -PathType Leaf) { Remove-Item -LiteralPath $oldFile }
                }
            }
        }
        foreach ($sourceInput in $inputs) {
            $relative = $sourceInput.FullName.Substring($Root.Length).TrimStart([char[]]@('\','/'))
            $target = Join-Path $Cache ('source/'+$relative)
            [System.IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
            Copy-Item -LiteralPath $sourceInput.FullName -Destination $target -Force
        }
        $previousGcServer = $env:DOTNET_gcServer
        $env:DOTNET_gcServer = '0'
        try { Invoke-WorkflowStep 'build-workflow' $dotnetPath @('build',(Join-Path $Cache 'source/tools/BenchForge.CheckWorkflow'),'-t:Rebuild','-c',$Configuration,'-r',$plan.rid,'--self-contained','false','--disable-build-servers','-m:1','-nr:false','-p:UseSharedCompilation=false',('-p:BaseIntermediateOutputPath='+(Join-Path $Cache 'obj/')),'-o',(Join-Path $Cache 'bin')) }
        finally { $env:DOTNET_gcServer = $previousGcServer }
        $artifacts = [ordered]@{}
        foreach ($name in $required) { $artifacts[$name] = Get-CheckFileHash (Join-Path $Cache ('bin/'+$name)) }
        Write-CheckAtomic $artifactManifest (ConvertTo-Json -InputObject $artifacts)
        Write-CheckAtomic $manifest (ConvertTo-Json -InputObject $relativeInputs)
        Write-CheckAtomic (Join-Path $Cache 'build-identity.json') (($plan | Select-Object *,@{Name='sdk';Expression={$sdk}}) | ConvertTo-Json)
        Write-CheckAtomic $fingerprintFile $fingerprint
    }
    if (!$PrepareOnly) { Invoke-WorkflowStep 'checks-and-focused-regressions' $dotnetPath @($dll,'--root',$Root,'--python',$Python,'--jobs',"$Jobs",'--timing',($Timing+'.checks.json'),'--command',$Command,'--expected-architecture',$plan.architecture,'--timeout-seconds',"$TimeoutSeconds") }
}
finally {
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent ([System.IO.Path]::GetFullPath($Timing)))) | Out-Null
    [System.IO.File]::WriteAllText($Timing,(ConvertTo-Json -InputObject @($stages.ToArray()) -Depth 8))
}
}
finally { $lock.Dispose() }
