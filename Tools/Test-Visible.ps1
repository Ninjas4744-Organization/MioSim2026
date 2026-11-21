param(
    [Parameter(Mandatory=$true)][string]$MoSimProject,
    [Parameter(Mandatory=$true)][string]$UnityEditor,
    [ValidateSet('Capacity','Pile','WheelPile','Test','Travel','Retention','Approach','Indexer','Systems','Jam','Moving','Delivery','Matrix','Pulse')]
    [string]$StartAt='Capacity'
)
$ErrorActionPreference='Stop'
$tasks=@('Capacity','Pile','WheelPile','Test','Travel','Retention','Approach','Indexer','Systems','Jam','Moving','Delivery','Matrix','Pulse')
$offset=[Array]::IndexOf($tasks,$StartAt)
foreach($task in $tasks[$offset..($tasks.Length-1)]){
    & (Join-Path $PSScriptRoot 'Invoke-Unity.ps1') -MoSimProject $MoSimProject -UnityEditor $UnityEditor -Task $task -Visible
    if(-not $?){throw "Visible regression failed: $task"}
    Write-Output ("Completed visible regression: $task at " + (Get-Date -Format o))
}
