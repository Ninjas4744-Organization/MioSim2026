param([Parameter(Mandatory=$true)][string]$MoSimProject)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$projectRoot=(Resolve-Path -LiteralPath $MoSimProject).Path
$reportPath=Join-Path $projectRoot 'BuildArtifacts/simple-controls.json'
$report=Get-Content -LiteralPath $reportPath -Raw|ConvertFrom-Json
$platform=Join-Path $projectRoot 'BuiltMods/rebuilt_ninjas_4744_turbo_2026/standalonewindows64'
$manifest=Get-Content -LiteralPath (Join-Path $platform 'modpack.json') -Raw|ConvertFrom-Json
$bundleHash=(Get-FileHash -LiteralPath (Join-Path $platform $manifest.id)).Hash.ToLowerInvariant()
$dllHash=(Get-FileHash -LiteralPath (Join-Path $platform $manifest.modDll)).Hash.ToLowerInvariant()
if(-not $report.passed -or $report.checks.Count -lt 58 -or $report.errors.Count -ne 0 -or $report.bundleSha256 -ne $bundleHash -or $report.runtimeDllSha256 -ne $dllHash){
    throw 'Run Build, then Controls with -DiagnosticArguments -turboBundle for this exact build before packaging.'
}
$files=@(Get-ChildItem -LiteralPath $platform -File)
if($files.Count -ne 6){throw 'Expected the six MoSim platform build files.'}
$label='Ninjas4744-Turbo2026-'+$manifest.version+'-simple-controls-test'
$staging=Join-Path $repoRoot ('dist/'+$label)
$mod=Join-Path $staging 'Ninjas4744-Turbo2026'
$destination=Join-Path $mod 'standalonewindows64'
New-Item -ItemType Directory -Path $destination -Force|Out-Null
$files|Copy-Item -Destination $destination -Force
Copy-Item -LiteralPath $reportPath -Destination (Join-Path $mod 'simple-controls.json') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination $mod -Force
@'
Turbo controls test build

Extract Ninjas4744-Turbo2026 into %USERPROFILE%\AppData\LocalLow\CascadeStudios\MoSim\Mods\.
Restart MoSim, enable Ninjas 4744 Turbo 2026, and select 4744 / Turbo.
Keep one Turbo mod version enabled.

Shift / left trigger: toggle intake deployment and collection.
K / right trigger: hold to aim and shoot; release to stop.
E / right bumper: hold to deliver. Q / left bumper: hold manual shoot.
Shooting leaves the intake open after the hopper empties and after release.
V / Y / Triangle: hard pumping, only while shooting. Collection never pumps.
Regular automatic pumping now spans about 42 mm, previously 21 mm.
Hard pumping retains its original stroke, force and speed settings.
Z / D-pad down: stow and cancel shooting. Release shoot before restarting.
Turbo is the default robot with hold-to-shoot and intake toggles.
Actual Turbo retains the original team's latched controls.

Validated against this exact bundle and DLL: at least 58 control checks covering
keyboard, gamepad, quick taps, aiming and feed readiness, release, intake deployment,
shooting-only pumping, pending-pump cancellation, physical three-ball shooting in
both variants, stow, disable, rebinding and the original latched controls. Asset and runtime
reference checks also passed. This focused test package does not certify that
all mechanical release gates pass. Consult RELEASE-STATUS.json when included.
'@|Set-Content -LiteralPath (Join-Path $mod 'BUILD-NOTES.txt') -Encoding utf8
$statusPath=Join-Path $repoRoot ('Reports/release-status-'+$manifest.version+'.json')
if(Test-Path -LiteralPath $statusPath){
    $status=Get-Content -LiteralPath $statusPath -Raw|ConvertFrom-Json
    if($status.artifacts.PSObject.Properties[$manifest.id].Value -eq $bundleHash -and $status.artifacts.PSObject.Properties[$manifest.modDll].Value -eq $dllHash){
        Copy-Item -LiteralPath $statusPath -Destination (Join-Path $mod 'RELEASE-STATUS.json') -Force
    }
}
$zip=Join-Path $repoRoot ('dist/'+$label+'-Windows.zip')
Compress-Archive -LiteralPath $mod -DestinationPath $zip -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::OpenRead($zip)
try {
    foreach($file in $files){
        $entry=$archive.GetEntry('Ninjas4744-Turbo2026/standalonewindows64/'+$file.Name)
        if(-not $entry){throw ('Missing ZIP entry: '+$file.Name)}
        $stream=$entry.Open()
        $sha=[Security.Cryptography.SHA256]::Create()
        try {$entryHash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant()}
        finally {$stream.Dispose();$sha.Dispose()}
        if($entryHash -ne (Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant()){throw ('ZIP hash mismatch: '+$file.Name)}
    }
}
finally {$archive.Dispose()}
Write-Output $zip
