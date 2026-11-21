param([Parameter(Mandatory=$true)][string]$MoSimProject)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$projectRoot=(Resolve-Path -LiteralPath $MoSimProject).Path
$artifacts=Join-Path $projectRoot 'BuildArtifacts'
$physics=Get-Content -LiteralPath (Join-Path $artifacts 'physics-diagnostics.json') -Raw|ConvertFrom-Json
$travel=Get-Content -LiteralPath (Join-Path $artifacts 'travel-after.json') -Raw|ConvertFrom-Json
$bundle=Get-Content -LiteralPath (Join-Path $artifacts 'bundle-audit.json') -Raw|ConvertFrom-Json
$retention=Get-Content -LiteralPath (Join-Path $artifacts 'retention-after.json') -Raw|ConvertFrom-Json
$systems=Get-Content -LiteralPath (Join-Path $artifacts 'systems-after.json') -Raw|ConvertFrom-Json
$jam=Get-Content -LiteralPath (Join-Path $artifacts 'systems-jam.json') -Raw|ConvertFrom-Json
$extra=@('approach-after.json','systems-moving-after.json','systems-delivery-after.json','systems-matrix-after.json','capacity-after.json','pile-after.json','wheel-pile-after.json','systems-pulse-after.json','systems-indexer-after.json')|ForEach-Object {Get-Content -LiteralPath (Join-Path $artifacts $_) -Raw|ConvertFrom-Json}
foreach($report in $extra){if(-not $report.passed){throw 'A moving, delivery, alliance or capacity check failed'}}
$indexer=$extra|Where-Object label -eq 'indexer-after'
$indexerCases=@('lower-pair','upper-pair','left-single','right-single','center-single','side-guides','outer-corners','asymmetric-guides','mixed-floors','blocked-drop','blocked-shooter')
if($indexer.cases.Count -ne $indexerCases.Count -or $indexer.simulationSpeed -ne 1 -or (Compare-Object ($indexerCases|Sort-Object) ($indexer.cases.indexerCase|Sort-Object))){throw 'Indexer must complete all eleven fixtures at normal simulation speed. Omit -IndexerCase for release verification.'}
$checks=@('completed','finiteAndStable','driveMoved','railsDeployed','pickedUp','shotReleased','disabledStopped','inputsBound','endToEndCycle','stowed','preloadsRetained','reverseSupportsLoad')
foreach($check in $checks){if(-not $physics.$check){throw ('Integration check failed: '+$check)}}
if($physics.errors.Count -gt 0 -or -not $travel.passed -or -not $bundle.passed -or -not $retention.passed -or -not $systems.passed -or -not $jam.passed){throw 'Verification reports are not passing. Run Build, Test, Travel, Retention, Systems, and Jam before packaging.'}
if($systems.cases.Count -ne 7 -or $systems.simulationSpeed -ne 1){throw 'Systems must complete the seven release cases at normal simulation speed.'}
$platform=Join-Path $projectRoot 'BuiltMods\rebuilt_ninjas_4744_turbo_2026\standalonewindows64'
$manifest=Get-Content -LiteralPath (Join-Path $platform 'modpack.json') -Raw|ConvertFrom-Json
if($retention.bundleSha256 -ne (Get-FileHash -LiteralPath (Join-Path $platform $manifest.id)).Hash.ToLower()){throw 'The retention report does not match this asset bundle. Run Retention again.'}
if($retention.runtimeDllSha256 -ne (Get-FileHash -LiteralPath (Join-Path $platform $manifest.modDll)).Hash.ToLower()){throw 'The retention report does not match this mod DLL. Run Retention again.'}
foreach($report in @($systems,$jam)+$extra){
    if($report.bundleSha256 -ne (Get-FileHash -LiteralPath (Join-Path $platform $manifest.id)).Hash.ToLower() -or $report.runtimeDllSha256 -ne (Get-FileHash -LiteralPath (Join-Path $platform $manifest.modDll)).Hash.ToLower()){throw 'A systems report does not match this bundle and DLL. Run Systems and Jam again.'}
}
$version=$manifest.version
$dist=Join-Path $repoRoot 'dist'
$staging=Join-Path $dist ('staging-'+$version)
$mod=Join-Path $staging 'Ninjas4744-Turbo2026'
$destination=Join-Path $mod 'standalonewindows64'
New-Item -ItemType Directory -Path $destination -Force|Out-Null
Get-ChildItem -LiteralPath $platform -File|Copy-Item -Destination $destination -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md'),(Join-Path $repoRoot 'CHANGELOG.md') -Destination $mod -Force
$windowsZip=Join-Path $dist ('Ninjas4744-Turbo2026-'+$version+'-Windows.zip')
Compress-Archive -LiteralPath $mod -DestinationPath $windowsZip -Force
Copy-Item -LiteralPath (Join-Path $artifacts 'Ninjas4744-Turbo2026.unitypackage') -Destination $dist -Force
$sourceZip=Join-Path $dist ('Ninjas4744-Turbo2026-'+$version+'-Editable.zip')
$sourcePaths=@('Assets','Models','Tools','Docs','Reports','Licenses','README.md','CHANGELOG.md','.gitignore','.gitattributes','.config','.editorconfig','.csharpierignore')|ForEach-Object{Join-Path $repoRoot $_}
Compress-Archive -LiteralPath $sourcePaths -DestinationPath $sourceZip -Force
Get-FileHash -LiteralPath $windowsZip,$sourceZip,(Join-Path $dist 'Ninjas4744-Turbo2026.unitypackage')|ForEach-Object{[PSCustomObject]@{file=Split-Path -Leaf $_.Path;sha256=$_.Hash.ToLower()}}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $dist 'checksums.json') -Encoding utf8
Write-Output $windowsZip
Write-Output $sourceZip
