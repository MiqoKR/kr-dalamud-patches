[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Version = '0.1.0',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'src\CustomizePlusKrActorPatcher\CustomizePlusKrActorPatcher.csproj'
$output = Join-Path $repositoryRoot 'dist\CustomizePlusKrActorPatcher'

$arguments = @(
    'publish', $project,
    '-c', $Configuration,
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    "-p:Version=$Version",
    "-p:FileVersion=$Version",
    '-o', $output
)

if ($NoRestore) {
    $arguments += '--no-restore'
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Customize+ KR 패처 빌드에 실패했습니다. (exit $LASTEXITCODE)"
}

$executable = Join-Path $output 'CustomizePlus.KR.Actor.Patcher.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "빌드 출력에서 실행 파일을 찾지 못했습니다: $executable"
}

Write-Host "빌드 완료: $executable"
Get-FileHash -LiteralPath $executable -Algorithm SHA256 | Format-List
