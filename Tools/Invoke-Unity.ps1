param(
    [Parameter(Mandatory=$true)][string]$MoSimProject,
    [Parameter(Mandatory=$true)][string]$UnityEditor,
    [ValidateSet('Assemble','Build','Test','Travel','Retention','Systems','Jam','Moving','Delivery','Matrix','Capacity','Pulse','Preview')][string]$Task='Build',
    [switch]$SyncOnly
)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$projectRoot=(Resolve-Path -LiteralPath $MoSimProject).Path
$editorPath=(Resolve-Path -LiteralPath $UnityEditor).Path
$versionFile=Join-Path $projectRoot 'ProjectSettings\ProjectVersion.txt'
if(-not(Test-Path -LiteralPath $versionFile)){throw 'MoSimProject must be the root of the official Unity project.'}
if((Get-Content -LiteralPath $versionFile -Raw)-notmatch '2023\.2\.22f1'){throw 'This mod targets Unity 2023.2.22f1. Use the pinned MoSim project.'}
$modRelative='Assets\Prefabs\Games\Rebuilt\Robots\Mods\Ninjas4744'
$modTarget=Join-Path $projectRoot $modRelative
New-Item -ItemType Directory -Path $modTarget -Force|Out-Null
Get-ChildItem -LiteralPath (Join-Path $repoRoot $modRelative) -Force|Copy-Item -Destination $modTarget -Recurse -Force
$folderMeta=Join-Path $repoRoot ($modRelative+'.meta')
if(Test-Path -LiteralPath $folderMeta){Copy-Item -LiteralPath $folderMeta -Destination ($modTarget+'.meta') -Force}
$editorTarget=Join-Path $projectRoot 'Assets\Editor'
New-Item -ItemType Directory -Path $editorTarget -Force|Out-Null
Get-ChildItem -LiteralPath (Join-Path $repoRoot 'Assets\Editor') -File|Copy-Item -Destination $editorTarget -Force
if($SyncOnly){Write-Output 'Turbo assets and editor tools synchronized.';return}

$methods=@{Assemble='Ninjas4744Builder.Assemble';Build='Ninjas4744Builder.Build';Test='Turbo4744Diagnostics.Run';Travel='Turbo4744TravelRegression.Run';Retention='Turbo4744RetentionRegression.Run';Systems='Turbo4744SystemsBench.Run';Jam='Turbo4744SystemsBench.Run';Moving='Turbo4744SystemsBench.Run';Delivery='Turbo4744SystemsBench.Run';Matrix='Turbo4744SystemsBench.Run';Capacity='Turbo4744CapacityBench.Run';Pulse='Turbo4744SystemsBench.Run';Preview='Ninjas4744Builder.RenderPreview'}
$artifactRoot=Join-Path $repoRoot 'Artifacts'
New-Item -ItemType Directory -Path $artifactRoot -Force|Out-Null
$logPath=Join-Path $artifactRoot ($Task+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.log')
$mappedDrive=$null
$runProject=$projectRoot
try {
    # Keep Unity's nested package paths short without moving the user's project.
    foreach($letter in @('M','N','P','Q','R','S')){
        if(-not(Get-PSDrive -Name $letter -ErrorAction SilentlyContinue)){
            $mappedDrive=$letter+':'
            subst $mappedDrive $projectRoot
            if($LASTEXITCODE -ne 0){throw 'Could not create a temporary short project path.'}
            $runProject=$mappedDrive+'/'
            break
        }
    }
    $arguments=@('-batchmode','-projectPath',('"'+$runProject+'"'),'-logFile',('"'+$logPath+'"'),'-executeMethod',$methods[$Task])
    if($Task -in @('Travel','Retention','Capacity')){$arguments+='-turboFixed'}
    if($Task -in @('Systems','Jam','Moving','Delivery','Matrix','Pulse')){$arguments+=@('-turboBundle','-turboStrict')}
    if($Task -eq 'Jam'){$arguments+='-turboJam'}
    if($Task -eq 'Moving'){$arguments+='-turboMoving'}
    if($Task -eq 'Delivery'){$arguments+='-turboDelivery'}
    if($Task -eq 'Matrix'){$arguments+='-turboMatrix'}
    if($Task -eq 'Pulse'){$arguments+='-turboPulse'}
    if($Task -in @('Assemble','Build','Preview')){$arguments+='-quit'}
    Write-Output ('Running '+$Task+'. Log: '+$logPath)
    $process=Start-Process -FilePath $editorPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if($process.ExitCode -ne 0){throw ('Unity exited with code '+$process.ExitCode+'. See '+$logPath)}
    if($Task -eq 'Build'){
        $syncArgs=@('-batchmode','-quit','-projectPath',('"'+$runProject+'"'),'-logFile',('"'+$logPath+'.runtime.log"'),'-executeMethod','Ninjas4744Builder.SyncRuntime')
        $sync=Start-Process -FilePath $editorPath -ArgumentList $syncArgs -WindowStyle Hidden -PassThru
        $sync.WaitForExit()
        if($sync.ExitCode -ne 0){throw 'Runtime synchronization failed; inspect the runtime log.'}
    }
    $buildArtifacts=Join-Path $projectRoot 'BuildArtifacts'
    foreach($name in @('assembly-audit.json','bundle-audit.json','physics-diagnostics.json','travel-after.json','retention-after.json','systems-after.json','systems-jam.json','systems-moving-after.json','systems-delivery-after.json','systems-matrix-after.json','capacity-after.json','systems-pulse-after.json','Turbo4744-unity-preview.png','Turbo4744-stowed.png','Turbo4744-deployed.png','Turbo4744-v4-closed.png','Turbo4744-v4-opened.png','Turbo4744-v4-loaded.png')){
        $file=Join-Path $buildArtifacts $name
        if(Test-Path -LiteralPath $file){Copy-Item -LiteralPath $file -Destination $artifactRoot -Force}
    }
    Get-ChildItem -LiteralPath $buildArtifacts -Filter 'Turbo4744-v5-*.png' -File|Copy-Item -Destination $artifactRoot -Force
    Write-Output ($Task+' passed.')
}
finally {if($mappedDrive){subst $mappedDrive /D}}
