param([Parameter(Mandatory=$true)][string]$MoSimProject)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$projectRoot=(Resolve-Path -LiteralPath $MoSimProject).Path
$artifacts=Join-Path $projectRoot 'BuildArtifacts'
$physics=Get-Content -LiteralPath (Join-Path $artifacts 'physics-diagnostics.json') -Raw|ConvertFrom-Json
$travel=Get-Content -LiteralPath (Join-Path $artifacts 'travel-after.json') -Raw|ConvertFrom-Json
$bundle=Get-Content -LiteralPath (Join-Path $artifacts 'bundle-audit.json') -Raw|ConvertFrom-Json
$checks=@('completed','finiteAndStable','driveMoved','railsDeployed','pickedUp','shotReleased','disabledStopped','inputsBound','endToEndCycle','stowed','preloadsRetained')
foreach($check in $checks){if(-not $physics.$check){throw ('Integration check failed: '+$check)}}
if($physics.errors.Count -gt 0 -or -not $travel.passed -or -not $bundle.passed){throw 'Verification reports are not passing. Run Build, Test, and Travel before packaging.'}
$platform=Join-Path $projectRoot 'BuiltMods\rebuilt_ninjas_4744_turbo_2026\standalonewindows64'
$manifest=Get-Content -LiteralPath (Join-Path $platform 'modpack.json') -Raw|ConvertFrom-Json
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
$sourcePaths=@('Assets','Models','Tools','Docs','Reports','Licenses','README.md','CHANGELOG.md','.gitignore','.gitattributes')|ForEach-Object{Join-Path $repoRoot $_}
Compress-Archive -LiteralPath $sourcePaths -DestinationPath $sourceZip -Force
Get-FileHash -LiteralPath $windowsZip,$sourceZip,(Join-Path $dist 'Ninjas4744-Turbo2026.unitypackage')|ForEach-Object{[PSCustomObject]@{file=Split-Path -Leaf $_.Path;sha256=$_.Hash.ToLower()}}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $dist 'checksums.json') -Encoding utf8
Write-Output $windowsZip
Write-Output $sourceZip
