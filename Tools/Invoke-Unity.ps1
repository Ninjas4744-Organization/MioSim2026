param(
    [Parameter(Mandatory=$true)][string]$MoSimProject,
    [Parameter(Mandatory=$true)][string]$UnityEditor,
    [ValidateSet('Assemble','Build','Test','Travel','Retention','Preview')][string]$Task='Build',
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

$methods=@{Assemble='Ninjas4744Builder.Assemble';Build='Ninjas4744Builder.Build';Test='Turbo4744Diagnostics.Run';Travel='Turbo4744TravelRegression.Run';Retention='Turbo4744RetentionRegression.Run';Preview='Ninjas4744Builder.RenderPreview'}
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
    if($Task -in @('Travel','Retention')){$arguments+='-turboFixed'}
    if($Task -in @('Assemble','Build','Preview')){$arguments+='-quit'}
    Write-Output ('Running '+$Task+'. Log: '+$logPath)
    $process=Start-Process -FilePath $editorPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if($process.ExitCode -ne 0){throw ('Unity exited with code '+$process.ExitCode+'. See '+$logPath)}
    $buildArtifacts=Join-Path $projectRoot 'BuildArtifacts'
    foreach($name in @('assembly-audit.json','bundle-audit.json','physics-diagnostics.json','travel-after.json','retention-after.json','Turbo4744-unity-preview.png','Turbo4744-stowed.png','Turbo4744-deployed.png')){
        $file=Join-Path $buildArtifacts $name
        if(Test-Path -LiteralPath $file){Copy-Item -LiteralPath $file -Destination $artifactRoot -Force}
    }
    Write-Output ($Task+' passed.')
}
finally {if($mappedDrive){subst $mappedDrive /D}}
