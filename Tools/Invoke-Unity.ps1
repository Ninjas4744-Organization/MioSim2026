param(
    [Parameter(Mandatory=$true)][string]$MoSimProject,
    [Parameter(Mandatory=$true)][string]$UnityEditor,
    [ValidateSet('Assemble','Simple','Steam','SteamInspect','SteamUpload','RailVelocity','Mobility','Controls','Performance','NetCollision','Build','Runtime','Test','Travel','Trench','Swerve','Traction','Retention','Approach','Systems','Jam','Moving','Delivery','Matrix','Capacity','Pile','WheelPile','Load','LoadedAim','Pulse','Indexer','Preview')][string]$Task='Build',
    [switch]$SyncOnly,
    [switch]$Authoring,
    [switch]$Baseline,
    [switch]$Heading90,
    [switch]$Visible,
    [switch]$MovingWithoutRearRim,
    [switch]$MovingDirectBrake,
    [switch]$AlignShooterHoods,
    [switch]$ClearRearShooterMouth,
    [string[]]$DiagnosticArguments,
    [string]$IndexerCase,
    [switch]$UseProjectSnapshot
)
$ErrorActionPreference='Stop'
if($MovingWithoutRearRim -and $Task -ne 'Moving'){throw '-MovingWithoutRearRim is an isolated Moving comparison.'}
if($MovingDirectBrake -and ($Task -ne 'Moving' -or $MovingWithoutRearRim)){throw '-MovingDirectBrake is an isolated Moving comparison.'}
if($AlignShooterHoods -and ($Task -notin @('Systems','Moving') -or $MovingWithoutRearRim -or $MovingDirectBrake)){throw '-AlignShooterHoods is an isolated Systems/Moving comparison.'}
if($ClearRearShooterMouth -and ($Task -notin @('Systems','Moving') -or $MovingWithoutRearRim -or $MovingDirectBrake)){throw '-ClearRearShooterMouth is an isolated Systems/Moving comparison.'}
$repoRoot=Split-Path -Parent $PSScriptRoot
$projectRoot=(Resolve-Path -LiteralPath $MoSimProject).Path
$editorPath=(Resolve-Path -LiteralPath $UnityEditor).Path
$versionFile=Join-Path $projectRoot 'ProjectSettings\ProjectVersion.txt'
if(-not(Test-Path -LiteralPath $versionFile)){throw 'MoSimProject must be the root of the official Unity project.'}
if((Get-Content -LiteralPath $versionFile -Raw)-notmatch '2023\.2\.22f1'){throw 'This mod targets Unity 2023.2.22f1. Use the pinned MoSim project.'}
$UseProjectSnapshot = $UseProjectSnapshot -or (Test-Path -LiteralPath (Join-Path $projectRoot '.turbo-source-snapshot'))
if($UseProjectSnapshot -and ($Task -in @('Assemble','Simple','Build','Runtime') -or $SyncOnly)){throw 'Snapshot mode is for validating an existing build. Remove .turbo-source-snapshot or omit -UseProjectSnapshot before authoring/building.'}
if(-not $UseProjectSnapshot){
$modRelative='Assets\Prefabs\Games\Rebuilt\Robots\Mods\Ninjas4744'
$modTarget=Join-Path $projectRoot $modRelative
New-Item -ItemType Directory -Path $modTarget -Force|Out-Null
Get-ChildItem -LiteralPath (Join-Path $repoRoot $modRelative) -Force|Copy-Item -Destination $modTarget -Recurse -Force
$folderMeta=Join-Path $repoRoot ($modRelative+'.meta')
if(Test-Path -LiteralPath $folderMeta){Copy-Item -LiteralPath $folderMeta -Destination ($modTarget+'.meta') -Force}
$editorTarget=Join-Path $projectRoot 'Assets\Editor'
New-Item -ItemType Directory -Path $editorTarget -Force|Out-Null
Get-ChildItem -LiteralPath (Join-Path $repoRoot 'Assets\Editor') -File|Copy-Item -Destination $editorTarget -Force
}
else {Write-Output 'Using the frozen project source snapshot.'}
if($SyncOnly){Write-Output 'Turbo assets and editor tools synchronized.';return}

$methods=@{Assemble='Ninjas4744Builder.Assemble';Simple='Ninjas4744Builder.BuildSimpleVariant';Steam='Turbo4744SteamRelease.Prepare';Controls='Turbo4744SimpleControlsRegression.Run';Build='Ninjas4744Builder.Build';Runtime='Ninjas4744Builder.SyncRuntime';Test='Turbo4744Diagnostics.Run';Travel='Turbo4744TravelRegression.Run';Trench='Turbo4744TrenchRegression.Run';Swerve='Turbo4744TrenchRegression.Run';Retention='Turbo4744RetentionRegression.Run';Approach='Turbo4744RetentionRegression.Run';Systems='Turbo4744SystemsBench.Run';Jam='Turbo4744SystemsBench.Run';Moving='Turbo4744SystemsBench.Run';Delivery='Turbo4744SystemsBench.Run';Matrix='Turbo4744SystemsBench.Run';Capacity='Turbo4744CapacityBench.Run';Pile='Turbo4744CapacityBench.Run';WheelPile='Turbo4744CapacityBench.Run';Load='Turbo4744CapacityBench.Run';LoadedAim='Turbo4744CapacityBench.Run';Pulse='Turbo4744SystemsBench.Run';Indexer='Turbo4744SystemsBench.Run';Preview='Ninjas4744Builder.RenderPreview'}
$methods['Performance']='Turbo4744HopperPerformanceBench.Run'
$methods['NetCollision']='Turbo4744NetCollisionRegression.Run'
$methods['Traction']='Turbo4744TrenchRegression.Run'
$methods['SteamInspect']='Turbo4744SteamUpload.Inspect'
$methods['SteamUpload']='Turbo4744SteamUpload.Run'
$methods['RailVelocity']='Turbo4744RailVelocityRegression.Run'
$methods['Mobility']='Turbo4744MobilityRegression.Run'
if($Task -eq 'Mobility'){
    $mobilityArtifacts=Join-Path $projectRoot 'BuildArtifacts'
    New-Item -ItemType Directory -Path $mobilityArtifacts -Force|Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot 'Reports/Drivability-1.0.1/captured-poses.json') -Destination (Join-Path $mobilityArtifacts 'mobility-captured-poses.json') -Force
}
$artifactRoot=Join-Path $repoRoot 'Artifacts'
New-Item -ItemType Directory -Path $artifactRoot -Force|Out-Null
$logPath=Join-Path $artifactRoot ($Task+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.log')
$mappedDrive=$null
$runProject=$projectRoot
try {
    # PowerShell's drive inventory can be stale when another shell creates a
    # subst mapping. Reserve paths against the OS, serializing concurrent runs.
    $driveLock=[Threading.Mutex]::new($false,'Local\MoSim2026UnityProjectPath')
    $driveLockHeld=$false
    try {
        $driveLockHeld=$driveLock.WaitOne(10000)
        if(-not $driveLockHeld){throw 'Timed out reserving a short Unity project path.'}
        foreach($mapping in @(& subst)){
            if($mapping -match '^([A-Z]):\\: => (.+)$' -and $matches[2].TrimEnd('\') -ieq $projectRoot.TrimEnd('\')){
                $runProject=$matches[1]+':/'
                break
            }
        }
        if($runProject -eq $projectRoot){
            foreach($letter in @('M','N','P','Q','R','S','T','U','V')){
                $candidate=$letter+':'
                if([IO.Directory]::Exists($candidate+'\')){continue}
                & subst $candidate $projectRoot
                if($LASTEXITCODE -ne 0){continue}
                $mappedDrive=$candidate
                $runProject=$mappedDrive+'/'
                break
            }
        }
    }
    finally {
        if($driveLockHeld){$driveLock.ReleaseMutex()}
        $driveLock.Dispose()
    }
    $arguments=@('-batchmode','-projectPath',('"'+$runProject+'"'),'-logFile',('"'+$logPath+'"'),'-executeMethod',$methods[$Task])
    if($Visible){$arguments=$arguments|Where-Object {$_ -ne '-batchmode'};$arguments+=@('-turboVisible','-turboTask',$Task)}
    if($Task -in @('Travel','Retention','Capacity')){$arguments+='-turboFixed'}
    if($Task -eq 'Approach'){$arguments+='-turboApproach'}
    if($Task -eq 'Controls' -and -not $Authoring){$arguments+='-turboBundle'}
    if($Task -eq 'SteamUpload'){$arguments+='-turboPublishSteam'}
    if($Task -eq 'Mobility'){$arguments+='-turboMobility'}
    if($Task -eq 'Approach' -and -not $Baseline){$arguments+='-turboFixed'}
    if($Task -in @('Trench','Traction') -and $Baseline){$arguments+='-turboBaseline'}
    if($Task -eq 'Swerve'){$arguments+='-turboSwerve'}
    if($Task -eq 'Traction'){$arguments+='-turboTraction'}
    if($Task -in @('Pile','WheelPile','Load','LoadedAim')){$arguments+='-turboPile';if(-not $Baseline){$arguments+='-turboFixed'}}
    if($Task -eq 'LoadedAim'){$arguments+='-turboLoadedAim'}
    if($Task -eq 'WheelPile'){$arguments+='-turboWheelPile'}
    if($Task -eq 'Load'){$arguments+='-turboLoadOnly'}
    if($Authoring){$arguments+='-turboAuthoring'}
    if($Heading90){$arguments+='-turboHeading90'}
    if($Task -in @('Systems','Jam','Moving','Delivery','Matrix','Pulse')){$arguments+=@('-turboBundle','-turboStrict')}
    if($Task -eq 'Jam'){$arguments+='-turboJam'}
    if($Task -eq 'Moving'){$arguments+='-turboMoving'}
    if($MovingWithoutRearRim){$arguments+='-turboSkipRearRim'}
    if($MovingDirectBrake){$arguments+='-turboDirectBrake'}
    if($AlignShooterHoods){$arguments+='-turboAlignHoods'}
    if($ClearRearShooterMouth){$arguments+='-turboClearRearMouth'}
    if($DiagnosticArguments){$arguments+=@('-turboDiagnostic')+$DiagnosticArguments}
    if($Task -eq 'Delivery'){$arguments+='-turboDelivery'}
    if($Task -eq 'Matrix'){$arguments+='-turboMatrix'}
    if($Task -eq 'Pulse'){$arguments+='-turboPulse'}
    if($Task -eq 'Indexer'){$arguments+=@('-turboIndexer','-turboStrict');if(-not $Authoring){$arguments+='-turboBundle'}}
    if($Task -eq 'Indexer' -and $IndexerCase){$arguments+=@('-turboIndexerCase',$IndexerCase)}
    if($Task -in @('Assemble','Simple','Steam','Build','Runtime','Preview','RailVelocity')){$arguments+='-quit'}
    Write-Output ('Running '+$Task+'. Log: '+$logPath)
    $windowStyle=if($Visible){'Normal'}else{'Hidden'}
    $process=Start-Process -FilePath $editorPath -ArgumentList $arguments -WindowStyle $windowStyle -PassThru
    $process.WaitForExit()
    if($process.ExitCode -ne 0){throw ('Unity exited with code '+$process.ExitCode+'. See '+$logPath)}
    if(Select-String -LiteralPath $logPath -Pattern 'executeMethod method .* threw exception\.|error CS\d+:' -Quiet){throw ('Unity task failed; see '+$logPath)}
    if($Task -in @('Assemble','Simple','Build')){
        # Keep generated prefab references with their source. Otherwise the next
        # test synchronization overwrites the assembled robot with the old prefab.
        foreach($file in Get-ChildItem -LiteralPath $modTarget -Recurse -File){
            if($file.Extension -notin @('.prefab','.asset','.mat','.meta')){continue}
            $relative=$file.FullName.Substring($modTarget.Length).TrimStart('\')
            if($Task -ne 'Assemble' -and $relative -notmatch '^(Ninjas4744\.asset(\.meta)?|Turbo4744\.asset(\.meta)?|Turbo4744Simple\.prefab(\.meta)?|TutorialCards\\Turbo Simple tutorial [1-3]\.prefab(\.meta)?)$'){continue}
            $destination=Join-Path (Join-Path $repoRoot $modRelative) $relative
            if((Test-Path -LiteralPath $destination) -and (Get-FileHash -LiteralPath $file.FullName).Hash -eq (Get-FileHash -LiteralPath $destination).Hash){continue}
            New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force|Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
        }
    }
    if($Task -eq 'Build'){
        $syncArgs=@('-batchmode','-quit','-projectPath',('"'+$runProject+'"'),'-logFile',('"'+$logPath+'.runtime.log"'),'-executeMethod','Ninjas4744Builder.SyncRuntime')
        $sync=Start-Process -FilePath $editorPath -ArgumentList $syncArgs -WindowStyle Hidden -PassThru
        $sync.WaitForExit()
        if($sync.ExitCode -ne 0){throw 'Runtime synchronization failed; inspect the runtime log.'}
    }
    $buildArtifacts=Join-Path $projectRoot 'BuildArtifacts'
    foreach($name in @('assembly-audit.json','bundle-audit.json','simple-controls.json','physics-diagnostics.json','travel-after.json','swerve-modules.json','trench-before.json','trench-after.json','retention-after.json','approach-before.json','approach-after.json','pile-before.json','pile-after.json','wheel-pile-after.json','load-after.json','systems-after.json','systems-jam.json','systems-moving-after.json','systems-delivery-after.json','systems-matrix-after.json','capacity-after.json','systems-pulse-after.json','systems-indexer-after.json','Turbo4744-unity-preview.png','Turbo4744-stowed.png','Turbo4744-deployed.png','Turbo4744-v4-closed.png','Turbo4744-v4-opened.png','Turbo4744-v4-loaded.png')){
        $file=Join-Path $buildArtifacts $name
        if(Test-Path -LiteralPath $file){Copy-Item -LiteralPath $file -Destination $artifactRoot -Force}
    }
    Get-ChildItem -LiteralPath $buildArtifacts -Filter 'Turbo4744-v5-*.png' -File|Copy-Item -Destination $artifactRoot -Force
    Get-ChildItem -LiteralPath $buildArtifacts -Filter 'Turbo4744-v6-*.png' -File|Copy-Item -Destination $artifactRoot -Force
    Get-ChildItem -LiteralPath $buildArtifacts -Filter 'hopper-performance-*.json' -File|Copy-Item -Destination $artifactRoot -Force
    Get-ChildItem -LiteralPath $buildArtifacts -Filter 'net-collision-*.json' -File|Copy-Item -Destination $artifactRoot -Force
    Get-ChildItem -LiteralPath $buildArtifacts -Filter 'traction-*.json' -File|Copy-Item -Destination $artifactRoot -Force
    Write-Output ($Task+' passed.')
}
finally {
    if($mappedDrive){
        foreach($mapping in @(& subst)){
            if($mapping -match '^([A-Z]):\\: => (.+)$' -and ($matches[1]+':') -eq $mappedDrive -and $matches[2].TrimEnd('\') -ieq $projectRoot.TrimEnd('\')){
                & subst $mappedDrive /D
                break
            }
        }
    }
}
