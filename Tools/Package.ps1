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
$checks=@('completed','finiteAndStable','driveMoved','railsDeployed','pickedUp','shotReleased','disabledStopped','inputsBound','endToEndCycle','stowed','preloadsRetained','reverseSupportsLoad','bellypanBlocksFuel','bellypanSupportsChassis','bellypanFieldContact')
foreach($check in $checks){if(-not $physics.$check){throw ('Integration check failed: '+$check)}}
if($physics.errors.Count -gt 0 -or -not $travel.passed -or -not $bundle.passed -or -not $retention.passed -or -not $systems.passed -or -not $jam.passed){throw 'Verification reports are not passing. Run Build, Test, Travel, Retention, Systems, and Jam before packaging.'}
if($systems.cases.Count -ne 7 -or $systems.simulationSpeed -ne 1){throw 'Systems must complete the seven release cases at normal simulation speed.'}
$platform=Join-Path $projectRoot 'BuiltMods\rebuilt_ninjas_4744_turbo_2026\standalonewindows64'
$manifest=Get-Content -LiteralPath (Join-Path $platform 'modpack.json') -Raw|ConvertFrom-Json

$currentBundleHash=(Get-FileHash -LiteralPath (Join-Path $platform $manifest.id)).Hash.ToLower()
$currentDllHash=(Get-FileHash -LiteralPath (Join-Path $platform $manifest.modDll)).Hash.ToLower()
$controls=Get-Content -LiteralPath (Join-Path $artifacts 'simple-controls.json') -Raw|ConvertFrom-Json
if(-not $controls.passed -or $controls.checks.Count -lt 58 -or $controls.errors.Count -ne 0 -or $controls.bundleSha256 -ne $currentBundleHash -or $controls.runtimeDllSha256 -ne $currentDllHash){throw 'Run all control, pumping and empty-hopper checks against this exact bundle and DLL before packaging.'}
$acceptedBundleHashes=@($currentBundleHash)
$artProofPath=Join-Path $repoRoot 'Reports/selector-artwork-equivalence-1.0.1.json'
if(Test-Path -LiteralPath $artProofPath){
    $artProof=Get-Content -LiteralPath $artProofPath -Raw|ConvertFrom-Json
    $artChanges=@($artProof.changed_objects)
    if($artProof.passed -and $artProof.scope -eq 'selector-artwork-only' -and $artProof.runtime_dll_identical -and $artProof.after_artifacts.PSObject.Properties[$manifest.id].Value -eq $currentBundleHash -and $artProof.after_artifacts.PSObject.Properties[$manifest.modDll].Value -eq $currentDllHash -and $artProof.before_artifacts.PSObject.Properties[$manifest.modDll].Value -eq $currentDllHash -and $artProof.unchanged_object_count -gt 2500 -and $artProof.object_count -eq ($artProof.unchanged_object_count+2) -and $artChanges.Count -eq 2 -and @($artChanges|Where-Object {$_.name -ne 'selector-logo' -or $_.type -notin @('Sprite','Texture2D')}).Count -eq 0 -and @($artChanges.type|Sort-Object -Unique).Count -eq 2){
        $acceptedBundleHashes+= $artProof.before_artifacts.PSObject.Properties[$manifest.id].Value
    }
}

if($retention.bundleSha256 -notin $acceptedBundleHashes){throw 'The retention report does not match this asset bundle. Run Retention again.'}
if($retention.runtimeDllSha256 -ne (Get-FileHash -LiteralPath (Join-Path $platform $manifest.modDll)).Hash.ToLower()){throw 'The retention report does not match this mod DLL. Run Retention again.'}
foreach($report in @($systems,$jam)+$extra){
    if($report.bundleSha256 -notin $acceptedBundleHashes -or $report.runtimeDllSha256 -ne (Get-FileHash -LiteralPath (Join-Path $platform $manifest.modDll)).Hash.ToLower()){throw 'A systems report does not match this bundle and DLL. Run Systems and Jam again.'}
}
$version=$manifest.version
if([version]$version -ge [version]'0.6.2'){
    $certificate=Get-Content -LiteralPath (Join-Path $repoRoot ('Reports/verification-'+$version+'.json')) -Raw|ConvertFrom-Json
    $native=Get-Content -LiteralPath (Join-Path $repoRoot ('Reports/native-trench-'+$version+'.json')) -Raw|ConvertFrom-Json
    $bump=Get-Content -LiteralPath (Join-Path $repoRoot ('Reports/native-bump-'+$version+'.json')) -Raw|ConvertFrom-Json
    $full=Get-Content -LiteralPath (Join-Path $repoRoot ('Reports/native-full-hopper-'+$version+'.json')) -Raw|ConvertFrom-Json
    $dllHash=(Get-FileHash -LiteralPath (Join-Path $platform $manifest.modDll)).Hash.ToLower()
    $bundleHash=(Get-FileHash -LiteralPath (Join-Path $platform $manifest.id)).Hash.ToLower()
    if($certificate.fixtures.Count -ne 14 -or @($certificate.fixtures|Where-Object {-not $_.passed -or (-not $_.embedded_hashes_match -and -not $_.gameplay_assets_match)}).Count -or $certificate.runtime_dll_sha256 -ne $dllHash -or $certificate.bundle_sha256 -ne $bundleHash){throw 'The complete release certificate must match this build. Run Tools/verify_release.py.'}
    if(-not $native.passed -or $native.cases.Count -lt 3 -or $native.artifacts.PSObject.Properties[$manifest.modDll].Value -ne $dllHash -or $native.artifacts.PSObject.Properties[$manifest.id].Value -ne $bundleHash){throw 'Native stowed trench verification must match this build. Run Tools/verify_native_trench.py.'}
    if(-not $full.passed -or $full.cases.Count -lt 2 -or $full.artifacts.PSObject.Properties[$manifest.modDll].Value -ne $dllHash -or $full.artifacts.PSObject.Properties[$manifest.id].Value -ne $bundleHash -or -not @($full.cases|Where-Object {$_.full_load -ge 60}).Count -or @($full.cases.simple_controls|Sort-Object -Unique).Count -ne 2){throw 'Native full-hopper verification must cover both variants, an actual 60+ ball load and recovery to empty stow. Run Tools/verify_native_full_hopper.py.'}
    if(-not $bump.passed -or $bump.artifacts.PSObject.Properties[$manifest.modDll].Value -ne $dllHash -or $bump.artifacts.PSObject.Properties[$manifest.id].Value -ne $bundleHash){throw 'Native bump and delivery verification must match this build. Run Tools/verify_native_bump.py.'}
}
$dist=Join-Path $repoRoot 'dist'
$staging=Join-Path $dist ('staging-'+$version)
$mod=Join-Path $staging 'Ninjas4744-Turbo2026'
$destination=Join-Path $mod 'standalonewindows64'
New-Item -ItemType Directory -Path $destination -Force|Out-Null
Get-ChildItem -LiteralPath $platform -File|Copy-Item -Destination $destination -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md'),(Join-Path $repoRoot 'CHANGELOG.md') -Destination $mod -Force
$hebrew=Join-Path $repoRoot 'Docs/Installation-Discord-he.md'
if(Test-Path -LiteralPath $hebrew){Copy-Item -LiteralPath $hebrew -Destination (Join-Path $mod 'INSTALL-he.md') -Force}
$windowsZip=Join-Path $dist ('Ninjas4744-Turbo2026-'+$version+'-Windows.zip')
Compress-Archive -LiteralPath $mod -DestinationPath $windowsZip -Force
$unityPackage=Join-Path $dist ('Ninjas4744-Turbo2026-'+$version+'.unitypackage')
Copy-Item -LiteralPath (Join-Path $artifacts 'Ninjas4744-Turbo2026.unitypackage') -Destination $unityPackage -Force
$sourceZip=Join-Path $dist ('Ninjas4744-Turbo2026-'+$version+'-Editable.zip')
$sourcePaths=@('Assets','Models','Tools','Docs','Reports','Licenses','README.md','CHANGELOG.md','.gitignore','.gitattributes','.config','.editorconfig','.csharpierignore')|ForEach-Object{Join-Path $repoRoot $_}
Compress-Archive -LiteralPath $sourcePaths -DestinationPath $sourceZip -Force
Get-FileHash -LiteralPath $windowsZip,$sourceZip,$unityPackage|ForEach-Object{[PSCustomObject]@{file=Split-Path -Leaf $_.Path;sha256=$_.Hash.ToLower()}}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $dist ('checksums-'+$version+'.json')) -Encoding utf8
Copy-Item -LiteralPath (Join-Path $dist ('checksums-'+$version+'.json')) -Destination (Join-Path $dist 'checksums.json') -Force
Write-Output $windowsZip
Write-Output $sourceZip
