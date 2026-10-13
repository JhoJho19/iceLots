param(
    [string]$UnityEditorDirectory = 'C:/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor',
    [string]$ConfigExport = 'Logs/Section413/config-reference.json'
)
$ErrorActionPreference = 'Stop'
$projectDirectory = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$probeDirectory = Join-Path $projectDirectory ('Logs/Section413/ProcessProbe-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $probeDirectory | Out-Null
$monoExecutable = Join-Path $UnityEditorDirectory 'Data/MonoBleedingEdge/bin/mono.exe'
$compiler = Join-Path $UnityEditorDirectory 'Data/MonoBleedingEdge/lib/mono/msbuild/Current/bin/Roslyn/csc.exe'
$netstandard = Join-Path $UnityEditorDirectory 'Data/MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'
$assemblies = @('IceP.Domain.dll', 'IceP.Application.dll', 'IceP.Infrastructure.dll')
foreach ($assembly in $assemblies) { Copy-Item -LiteralPath (Join-Path $projectDirectory "Library/ScriptAssemblies/$assembly") -Destination $probeDirectory }
$jsonAssembly = (Get-ChildItem -LiteralPath (Join-Path $projectDirectory 'Library/PackageCache') -Filter 'Newtonsoft.Json.dll' -Recurse | Where-Object FullName -NotMatch '/AOT/|\\AOT\\' | Select-Object -First 1).FullName
Copy-Item -LiteralPath $jsonAssembly -Destination (Join-Path $probeDirectory 'Newtonsoft.Json.dll')
$references = @($assemblies | ForEach-Object { '/reference:' + (Join-Path $probeDirectory $_) })
$references += '/reference:' + (Join-Path $probeDirectory 'Newtonsoft.Json.dll')
$references += '/reference:' + $netstandard
$executable = Join-Path $probeDirectory 'ProcessLifecycleProbe.exe'
& $monoExecutable $compiler /nologo /langversion:latest ('/out:' + $executable) @references (Join-Path $PSScriptRoot 'ProcessLifecycleProbe.cs')
if ($LASTEXITCODE -ne 0) { throw 'Probe compilation failed.' }
$export = (Resolve-Path (Join-Path $projectDirectory $ConfigExport)).Path
foreach ($mode in @('before', 'paid', 'free', 'settled')) {
    $saveDirectory = Join-Path $probeDirectory $mode
    $start = [Diagnostics.ProcessStartInfo]::new($monoExecutable)
    $start.Arguments = '"' + $executable + '" "' + $export + '" "' + $saveDirectory + '" ' + $mode
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        while (!(Test-Path -LiteralPath (Join-Path $saveDirectory 'ready.json')) -and !$process.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 50 }
        if ($process.HasExited) { throw ($process.StandardError.ReadToEnd()) }
        if (!(Test-Path -LiteralPath (Join-Path $saveDirectory 'ready.json'))) { throw 'Timed out waiting for committed checkpoint.' }
        $probePid = $process.Id
        $process.Kill(); $process.WaitForExit()
        Write-Output "Force-killed owned production-service probe PID $probePid at $mode checkpoint."
        & $monoExecutable $executable $export $saveDirectory $mode verify
        if ($LASTEXITCODE -ne 0) { throw "Recovery verification failed: $mode" }
        & $monoExecutable $executable $export $saveDirectory $mode verify
        if ($LASTEXITCODE -ne 0) { throw "Second restart verification failed: $mode" }
    } finally {
        if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
}
Write-Output "Evidence: $probeDirectory. This is a real OS kill of production economic-service processes, not a graphical Unity player or power-loss test."
