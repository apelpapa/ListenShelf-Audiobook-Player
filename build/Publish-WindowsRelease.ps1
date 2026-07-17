[CmdletBinding()]
param(
    [Parameter()]
    [ValidatePattern('^[0-9A-Za-z.-]+$')]
    [string] $Version = '0.1.0-alpha.1'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'src\ListenShelf.Desktop\ListenShelf.Desktop.csproj'
$wixSourcePath = Join-Path $repoRoot 'packaging\windows\ListenShelf.wxs'
$releaseBasePath = Join-Path $repoRoot 'artifacts\release'
$releaseRoot = Join-Path $releaseBasePath "v$Version"
$workRoot = Join-Path $releaseRoot 'work'
$outputRoot = Join-Path $releaseRoot 'assets'
$portableStage = Join-Path $workRoot 'portable'
$singleFileStage = Join-Path $workRoot 'single-file'

$numericVersion = ($Version -split '-', 2)[0]
if ($numericVersion -notmatch '^\d+\.\d+\.\d+$')
{
    throw "The installer requires a three-part numeric version. Received '$numericVersion'."
}

$normalizedReleaseBase = [IO.Path]::GetFullPath($releaseBasePath)
$normalizedReleaseRoot = [IO.Path]::GetFullPath($releaseRoot)
if (-not $normalizedReleaseRoot.StartsWith(
        $normalizedReleaseBase + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase))
{
    throw 'The computed release path is outside the repository artifacts directory.'
}

if (Test-Path -LiteralPath $releaseRoot)
{
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $portableStage, $singleFileStage, $outputRoot -Force | Out-Null

$commonPublishArguments = @(
    'publish',
    $projectPath,
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    "-p:Version=$Version",
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-p:VlcWindowsX64Enabled=true',
    '-p:VlcWindowsX86Enabled=false',
    '-p:VlcWindowsArm64Enabled=false'
)

Write-Host 'Publishing the Windows x64 portable folder...'
& dotnet @commonPublishArguments '--output' $portableStage '-p:PublishSingleFile=false'
if ($LASTEXITCODE -ne 0)
{
    throw "Portable publish failed with exit code $LASTEXITCODE."
}

Get-ChildItem -LiteralPath $portableStage -Recurse -Filter '*.pdb' -File |
    Remove-Item -Force

$portableZipName = "ListenShelf-$Version-win-x64-portable.zip"
$portableZipPath = Join-Path $outputRoot $portableZipName
Compress-Archive -Path (Join-Path $portableStage '*') -DestinationPath $portableZipPath -CompressionLevel Optimal

Write-Host 'Publishing the Windows x64 single-file executable...'
& dotnet @commonPublishArguments `
    '--output' $singleFileStage `
    '-p:PublishSingleFile=true' `
    '-p:IncludeNativeLibrariesForSelfExtract=true' `
    '-p:IncludeAllContentForSelfExtract=true'
if ($LASTEXITCODE -ne 0)
{
    throw "Single-file publish failed with exit code $LASTEXITCODE."
}

$singleFileSource = Join-Path $singleFileStage 'ListenShelf.exe'
$singleFileName = "ListenShelf-$Version-win-x64-single-file.exe"
$singleFilePath = Join-Path $outputRoot $singleFileName
Copy-Item -LiteralPath $singleFileSource -Destination $singleFilePath

Write-Host 'Restoring WiX and building the Windows installer...'
Push-Location $repoRoot
try
{
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0)
    {
        throw "WiX tool restore failed with exit code $LASTEXITCODE."
    }

    $installerName = "ListenShelf-$Version-win-x64-installer.msi"
    $installerPath = Join-Path $outputRoot $installerName
    & dotnet tool run wix -- build `
        $wixSourcePath `
        '-arch' 'x64' `
        '-d' "ProductVersion=$numericVersion" `
        '-bindpath' "PublishDir=$portableStage" `
        '-o' $installerPath
    if ($LASTEXITCODE -ne 0)
    {
        throw "Installer build failed with exit code $LASTEXITCODE."
    }

    Get-ChildItem -LiteralPath $outputRoot -Filter '*.wixpdb' -File |
        Remove-Item -Force
}
finally
{
    Pop-Location
}

$releaseAssets = Get-ChildItem -LiteralPath $outputRoot -File |
    Where-Object { $_.Extension -in '.exe', '.msi', '.zip' } |
    Sort-Object Name

$checksumLines = foreach ($asset in $releaseAssets)
{
    $hash = Get-FileHash -LiteralPath $asset.FullName -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $($asset.Name)"
}

$checksumPath = Join-Path $outputRoot 'SHA256SUMS.txt'
$checksumLines | Set-Content -LiteralPath $checksumPath -Encoding ascii

Write-Host ''
Write-Host "Release assets created in $outputRoot"
Get-ChildItem -LiteralPath $outputRoot -File |
    Select-Object Name, @{ Name = 'SizeMB'; Expression = { [Math]::Round($_.Length / 1MB, 1) } }
