param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRelative = "publish/SegmentPlayer-win-x64-portable"
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Path $MyInvocation.MyCommand.Path -Parent
$projectRoot = (Resolve-Path (Join-Path $scriptRoot "..")).Path
$projectFile = Join-Path $projectRoot "PortablePlayer.csproj"
$launcherProject = Join-Path $projectRoot "Launcher/SegmentPlayerLauncher.csproj"
$outputDir = Join-Path $projectRoot $OutputRelative
$appDir = Join-Path $outputDir "app"

Write-Host "ProjectRoot: $projectRoot"
Write-Host "OutputDir  : $outputDir"

if (Test-Path $outputDir)
{
    Remove-Item $outputDir -Recurse -Force
}

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
New-Item -ItemType Directory -Path $appDir -Force | Out-Null

$satelliteLang = "zh-Hans%3Bzh-Hant%3Ben"
dotnet publish $projectFile `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -p:SatelliteResourceLanguages=$satelliteLang `
    -o $appDir
if ($LASTEXITCODE -ne 0)
{
    throw "main app publish failed with exit code $LASTEXITCODE"
}

dotnet publish $launcherProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $outputDir
if ($LASTEXITCODE -ne 0)
{
    throw "launcher publish failed with exit code $LASTEXITCODE"
}

New-Item -ItemType Directory -Path (Join-Path $appDir "media_groups") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $appDir "cache/thumbnails") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $appDir "logs") -Force | Out-Null

$readme = @'
SegmentPlayer Portable Package

How to launch:
1. Double-click SegmentPlayer.exe

Directory layout:
- SegmentPlayer.exe : root launcher
- app\ : player runtime and dependencies
- app\config\ : settings file
- app\media_groups\ : media group root
- app\cache\thumbnails\ : thumbnail cache
- app\logs\ : runtime logs
'@
Set-Content -Path (Join-Path $outputDir "README.txt") -Value $readme -Encoding ASCII

Write-Host "Portable package created."
Write-Host "Launcher: $(Join-Path $outputDir 'SegmentPlayer.exe')"
