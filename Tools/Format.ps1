param([switch]$Check)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $repoRoot '.dotnet\dotnet.exe'
$usingLocalSdk = Test-Path -LiteralPath $dotnet
if (-not $usingLocalSdk) {
    $installedDotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $installedDotnet) {
        throw 'Install the .NET 10 SDK before running the formatter. See README.md.'
    }
    $dotnet = $installedDotnet.Source
}

$previousPath = $env:PATH
$previousDotnetRoot = $env:DOTNET_ROOT
$previousDotnetRootX64 = $env:DOTNET_ROOT_X64
$previousCliHome = $env:DOTNET_CLI_HOME
$previousNugetPackages = $env:NUGET_PACKAGES
Push-Location -LiteralPath $repoRoot
try {
    # Let the restored tool find the same runtime as the SDK, including a cached SDK.
    $env:DOTNET_ROOT = Split-Path -Parent $dotnet
    $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
    $env:PATH = $env:DOTNET_ROOT + [IO.Path]::PathSeparator + $env:PATH
    if ($usingLocalSdk) {
        $env:DOTNET_CLI_HOME = Join-Path $repoRoot '.dotnet\cli-home'
        $env:NUGET_PACKAGES = Join-Path $repoRoot '.dotnet\packages'
    }

    & $dotnet tool restore
    if ($LASTEXITCODE -ne 0) {
        throw 'CSharpier restore failed. Ensure the .NET 10 SDK is installed.'
    }

    $command = if ($Check) { 'check' } else { 'format' }
    & $dotnet csharpier $command .
    if ($LASTEXITCODE -ne 0) {
        throw "CSharpier $command failed."
    }
}
finally {
    Pop-Location
    $env:PATH = $previousPath
    $env:DOTNET_ROOT = $previousDotnetRoot
    $env:DOTNET_ROOT_X64 = $previousDotnetRootX64
    $env:DOTNET_CLI_HOME = $previousCliHome
    $env:NUGET_PACKAGES = $previousNugetPackages
}
