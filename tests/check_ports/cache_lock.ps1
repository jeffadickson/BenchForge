# Focused bounded lock tests; no application tests, hardware access or builds.
param([string]$Output = 'build/check-ports/cache-lock.json')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $root 'scripts/check-build.ps1')
$folder = Join-Path $root ('build/check-lock-tests/' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($folder) | Out-Null
$path = Join-Path $folder 'build.lock'
$results = [System.Collections.Generic.List[object]]::new()
function Case([string]$Name, [scriptblock]$Action) { & $Action; $results.Add([pscustomobject]@{case=$Name;result='passed'}) }
try {
    Case 'Immediate acquisition and repeat reuse' {
        $held = Get-CheckCacheLock $path 1000
        if ($held -isnot [System.IO.FileStream]) { throw 'Expected owned file handle' }
        $held.Dispose(); $again = Get-CheckCacheLock $path 1000; $again.Dispose()
    }
    Case 'Contention waits and times out without modifying cache' {
        [System.IO.File]::WriteAllText($path,'retained')
        $held = Get-CheckCacheLock $path 1000
        try {
            $watch = [System.Diagnostics.Stopwatch]::StartNew();$timedOut=$false
            try { $unexpected = Get-CheckCacheLock $path 120; $unexpected.Dispose() }
            catch [System.TimeoutException] { $timedOut=$true }
            if (!$timedOut -or $watch.ElapsedMilliseconds -lt 100) { throw 'Lock contention did not honor its wait budget' }
        } finally { $held.Dispose() }
        if ([System.IO.File]::ReadAllText($path) -ne 'retained') { throw 'Contender changed lock contents' }
    }
    Add-Type -TypeDefinition 'public static class CheckLockRelease { public static System.Threading.Tasks.Task Later(System.IO.FileStream stream) { return System.Threading.Tasks.Task.Run(async () => { await System.Threading.Tasks.Task.Delay(150); stream.Dispose(); }); } }'
    Case 'Released contention becomes available before deadline' {
        $held=Get-CheckCacheLock $path 1000;$release=[CheckLockRelease]::Later($held)
        $next=Get-CheckCacheLock $path 10000
        try { $release.GetAwaiter().GetResult() | Out-Null } finally { $next.Dispose() }
    }
    Case 'Invalid budgets fail before creating lock files' {
        foreach ($budget in @(0,-1,3600001)) {
            $invalid=Join-Path $folder ('invalid-'+$budget);$rejected=$false
            try { $unexpected=Get-CheckCacheLock $invalid $budget; $unexpected.Dispose() } catch { $rejected=$true }
            if (!$rejected -or (Test-Path -LiteralPath $invalid)) { throw 'Invalid budget mutated files' }
        }
    }
    Case 'Non-contention I/O errors propagate' {
        $rejected=$false
        try { $unexpected=Get-CheckCacheLock (Join-Path $folder 'missing/build.lock') 1000; $unexpected.Dispose() }
        catch [System.IO.DirectoryNotFoundException] { $rejected=$true }
        if (!$rejected) { throw 'Expected missing parent error' }
    }
} finally {
    # All handles are disposed; remove only this explicitly owned test directory.
    $resolved=[System.IO.Path]::GetFullPath($folder)
    $parent=[System.IO.Path]::GetFullPath((Join-Path $root 'build/check-lock-tests'))+[System.IO.Path]::DirectorySeparatorChar
    if (!$resolved.StartsWith($parent,[System.StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid owned test directory' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
[System.IO.Directory]::CreateDirectory((Split-Path -Parent ([System.IO.Path]::GetFullPath($Output)))) | Out-Null
[System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($Output),(ConvertTo-Json -InputObject @($results.ToArray()) -Depth 5))
Write-Output "PASS: $($results.Count) cache lock contracts"
