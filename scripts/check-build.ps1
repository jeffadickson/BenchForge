# Shared build identity and cache validation; dot-sourcing performs no build.
Set-StrictMode -Version Latest

function Get-CheckCacheLock([string]$Path, [int]$TimeoutMilliseconds = 300000) {
    if ($TimeoutMilliseconds -lt 1 -or $TimeoutMilliseconds -gt 3600000) { throw 'Invalid cache lock wait budget' }
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        try { return [System.IO.File]::Open($Path,[System.IO.FileMode]::OpenOrCreate,[System.IO.FileAccess]::ReadWrite,[System.IO.FileShare]::None) }
        catch [System.IO.IOException] {
            $failure = $_.Exception
            while ($failure.InnerException) { $failure = $failure.InnerException }
            $code = $failure.HResult -band 0xffff
            if ($code -notin @(11,32,33)) { throw }
            if ($watch.ElapsedMilliseconds -ge $TimeoutMilliseconds) { throw [System.TimeoutException]::new('Timed out waiting for the compiled check cache lock.', $failure) }
            [System.Threading.Thread]::Sleep([int][Math]::Min(50,[Math]::Max(1,$TimeoutMilliseconds-$watch.ElapsedMilliseconds)))
        }
    }
}

function Get-CheckBuildPlan {
    param(
        [string]$Platform,
        [string]$HostArchitecture,
        [string]$Architecture = 'auto',
        [string]$Configuration = 'Release'
    )
    $platforms = @('win','linux','osx')
    $architectures = @('x64','x86','arm64','arm')
    if ($Platform -cnotin $platforms) { throw "Unsupported check platform: $Platform" }
    if ($HostArchitecture -cnotin $architectures) { throw "Unsupported host architecture: $HostArchitecture" }
    if ($Architecture -ceq 'auto') { $Architecture = $HostArchitecture }
    if ($Architecture -cnotin $architectures) { throw "Unsupported check architecture: $Architecture" }
    if ($Configuration -cnotin @('Debug','Release')) { throw "Unsupported check configuration: $Configuration" }
    $supported = @{ win=@('x64','x86','arm64'); linux=@('x64','arm64','arm'); osx=@('x64','arm64') }
    if ($Architecture -cnotin $supported[$Platform]) { throw "Unsupported check runtime: $Platform-$Architecture" }
    [pscustomobject]@{
        schema=1; platform=$Platform; architecture=$Architecture
        configuration=$Configuration; rid="$Platform-$Architecture"
        cache_segment="$Platform-$Architecture/$Configuration"
        can_execute=($Architecture -ceq $HostArchitecture)
    }
}

function Get-CheckFileHash([string]$Path) {
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::OpenRead($Path)
    try { [System.BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-','').ToLowerInvariant() }
    finally { $stream.Dispose(); $algorithm.Dispose() }
}

function Resolve-CheckOwnedPath([string]$Directory, [string]$Relative) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or [System.IO.Path]::IsPathRooted($Relative)) { throw 'Invalid cache source manifest path' }
    $parent = [System.IO.Path]::GetFullPath($Directory).TrimEnd([char[]]@('\','/')) + [System.IO.Path]::DirectorySeparatorChar
    $resolved = [System.IO.Path]::GetFullPath((Join-Path $parent $Relative))
    $comparison = if ([System.IO.Path]::DirectorySeparatorChar -eq '\') { [System.StringComparison]::OrdinalIgnoreCase } else { [System.StringComparison]::Ordinal }
    if (!$resolved.StartsWith($parent,$comparison)) { throw 'Invalid cache source manifest path' }
    $resolved
}

function Write-CheckAtomic([string]$Path, [string]$Content) {
    $temporary = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [System.IO.File]::WriteAllText($temporary,$Content)
        Move-Item -LiteralPath $temporary -Destination $Path -Force
    }
    finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
}
