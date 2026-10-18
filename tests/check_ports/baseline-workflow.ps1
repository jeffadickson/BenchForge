param([string]$Root,[string]$Cache,[string]$Timing,[string]$Python='python')
$ErrorActionPreference='Stop'
$stages=[System.Collections.Generic.List[object]]::new()
function Run-Step([string]$Name,[string]$Exe,[string[]]$Arguments) {
    $watch=[System.Diagnostics.Stopwatch]::StartNew()
    $savedPreference=$ErrorActionPreference
    $ErrorActionPreference="Continue"
    & $Exe @Arguments | Out-Host
    $ErrorActionPreference=$savedPreference
    $code=$LASTEXITCODE
    $watch.Stop()
    $stages.Add([pscustomobject]@{name=$Name;milliseconds=$watch.Elapsed.TotalMilliseconds;exit_code=$code})
    $stages | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Timing -Encoding UTF8
    if ($code -ne 0) { throw "$Name failed with exit code $code" }
}
Set-Location -LiteralPath $Root
$native=Join-Path $Cache 'native/BenchForge.Checks.exe'
$roslyn=Join-Path $Cache 'source/tools/BenchDouble.DocumentationAudit/bin/Release/net10.0/BenchDouble.DocumentationAudit.dll'
if (!(Test-Path -LiteralPath $native)) {
    Run-Step 'build-native' 'dotnet' @('publish',(Join-Path $Cache 'source/tools/BenchForge.Checks'),'-c','Release','-p:PublishAot=true','-p:IlcOptimizationPreference=Speed','-p:ILLinkTreatWarningsAsErrors=true','-o',(Join-Path $Cache 'native'))
}
if (!(Test-Path -LiteralPath $roslyn)) {
    Run-Step 'build-roslyn' 'dotnet' @('build',(Join-Path $Cache 'source/tools/BenchDouble.DocumentationAudit'),'-c','Release')
}
Run-Step 'requirements' $native @('requirements','--jobs','8')
Run-Step 'scoreboard' $native @('scoreboard','--jobs','1')
Run-Step 'neutrality' $native @('neutrality','--jobs','8','--strict','--output','build/source-neutrality-audit.json')
Run-Step 'roslyn' 'dotnet' @($roslyn,'.','build/api-audit.json','--jobs','8')
Run-Step 'preflight' $native @('preflight','--jobs','8','--python',$Python,'--output','build/docs-preflight.json')
Run-Step 'docs' $Python @('scripts/check_api_docs.py')
Run-Step 'compare' $native @('compare','--jobs','8','--roslyn','build/api-audit.json','--portable','build/docs-preflight.json','--output','build/docs-parity.json')
Run-Step 'tracker-tests' $Python @('tests/check_ports/workflow_benchmark.py','--legacy-tracker',(Join-Path $Cache 'source/tests/requirements/test_gate.py'))
Run-Step 'documentation-tests' $Python @('-m','unittest','discover','-s','tests/documentation','-v')
$stages | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Timing -Encoding UTF8
