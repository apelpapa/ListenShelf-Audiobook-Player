[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('linux-x64', 'osx-arm64', 'osx-x64')]
    [string] $RuntimeIdentifier,

    [Parameter()]
    [ValidatePattern('^[0-9A-Za-z.-]+$')]
    [string] $Version = '0.1.0-alpha.2'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'src/ListenShelf.Desktop/ListenShelf.Desktop.csproj'
$artifactBasePath = Join-Path $repoRoot 'artifacts/test-builds'
$artifactRoot = Join-Path $artifactBasePath "v$Version/$RuntimeIdentifier"
$publishRoot = Join-Path $artifactRoot 'work/publish'
$assetsRoot = Join-Path $artifactRoot 'assets'

$expectedHost = if ($RuntimeIdentifier.StartsWith('linux-')) { 'Linux' } else { 'macOS' }
$hostMatches = if ($expectedHost -eq 'Linux')
{
    [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [Runtime.InteropServices.OSPlatform]::Linux)
}
else
{
    [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [Runtime.InteropServices.OSPlatform]::OSX)
}
if (-not $hostMatches)
{
    throw "$RuntimeIdentifier must be packaged on $expectedHost so native file permissions and bundle structure are preserved."
}

$normalizedBase = [IO.Path]::GetFullPath($artifactBasePath)
$normalizedRoot = [IO.Path]::GetFullPath($artifactRoot)
if (-not $normalizedRoot.StartsWith(
        $normalizedBase + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase))
{
    throw 'The computed test-build path is outside the repository artifacts directory.'
}

$numericVersion = ($Version -split '-', 2)[0]
if ($numericVersion -notmatch '^\d+\.\d+\.\d+$')
{
    throw "The app bundle requires a three-part numeric version. Received '$numericVersion'."
}

if (Test-Path -LiteralPath $artifactRoot)
{
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $publishRoot, $assetsRoot -Force | Out-Null

Write-Host "Publishing the $RuntimeIdentifier test build on $expectedHost..."
& dotnet publish `
    $projectPath `
    '--configuration' 'Release' `
    '--runtime' $RuntimeIdentifier `
    '--self-contained' 'true' `
    '--output' $publishRoot `
    "-p:Version=$Version" `
    '-p:DebugType=None' `
    '-p:DebugSymbols=false' `
    '-p:PublishSingleFile=false'
if ($LASTEXITCODE -ne 0)
{
    throw "Publish failed with exit code $LASTEXITCODE."
}

Get-ChildItem -LiteralPath $publishRoot -Recurse -Filter '*.pdb' -File |
    Remove-Item -Force

if ($RuntimeIdentifier.StartsWith('linux-'))
{
    if (-not (Get-Command zip -ErrorAction SilentlyContinue))
    {
        throw "The 'zip' command is required so Linux executable permissions are retained."
    }

    $folderName = "ListenShelf-$Version-$RuntimeIdentifier"
    $packageRoot = Join-Path $artifactRoot "work/$folderName"
    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
    Copy-Item -Path (Join-Path $publishRoot '*') -Destination $packageRoot -Recurse
    Copy-Item -LiteralPath (Join-Path $repoRoot 'packaging/linux/TEST-BUILD-README.txt') -Destination $packageRoot
    Copy-Item -LiteralPath (Join-Path $repoRoot 'packaging/linux/listenshelf.desktop') -Destination $packageRoot
    Copy-Item -LiteralPath (Join-Path $repoRoot 'src/ListenShelf.Desktop/Assets/Branding/listenshelf-1024.png') -Destination (Join-Path $packageRoot 'listenshelf.png')
    & chmod '+x' (Join-Path $packageRoot 'ListenShelf')
    if ($LASTEXITCODE -ne 0)
    {
        throw "chmod failed with exit code $LASTEXITCODE."
    }

    $zipPath = Join-Path $assetsRoot "$folderName.zip"
    Push-Location (Split-Path -Parent $packageRoot)
    try
    {
        & zip '-q' '-r' $zipPath $folderName
        if ($LASTEXITCODE -ne 0)
        {
            throw "zip failed with exit code $LASTEXITCODE."
        }
    }
    finally
    {
        Pop-Location
    }
}
else
{
    if (-not (Get-Command ditto -ErrorAction SilentlyContinue))
    {
        throw "The macOS 'ditto' command is required to preserve the application bundle."
    }

    $folderName = "ListenShelf-$Version-$RuntimeIdentifier"
    $packageRoot = Join-Path $artifactRoot "work/$folderName"
    $appRoot = Join-Path $packageRoot 'ListenShelf.app'
    $contentsRoot = Join-Path $appRoot 'Contents'
    $macOsRoot = Join-Path $contentsRoot 'MacOS'
    $resourcesRoot = Join-Path $contentsRoot 'Resources'
    New-Item -ItemType Directory -Path $macOsRoot, $resourcesRoot -Force | Out-Null
    Copy-Item -Path (Join-Path $publishRoot '*') -Destination $macOsRoot -Recurse
    Copy-Item -LiteralPath (Join-Path $repoRoot 'src/ListenShelf.Desktop/Assets/Branding/listenshelf.icns') -Destination $resourcesRoot
    Copy-Item -LiteralPath (Join-Path $repoRoot 'packaging/macos/TEST-BUILD-README.txt') -Destination $packageRoot

    $plistTemplate = Get-Content -LiteralPath (Join-Path $repoRoot 'packaging/macos/Info.plist') -Raw
    $plist = $plistTemplate.Replace('__NUMERIC_VERSION__', $numericVersion)
    [IO.File]::WriteAllText(
        (Join-Path $contentsRoot 'Info.plist'),
        $plist,
        [Text.UTF8Encoding]::new($false))
    & chmod '+x' (Join-Path $macOsRoot 'ListenShelf')
    if ($LASTEXITCODE -ne 0)
    {
        throw "chmod failed with exit code $LASTEXITCODE."
    }

    $zipPath = Join-Path $assetsRoot "$folderName.zip"
    & ditto '-c' '-k' '--sequesterRsrc' '--keepParent' $packageRoot $zipPath
    if ($LASTEXITCODE -ne 0)
    {
        throw "ditto failed with exit code $LASTEXITCODE."
    }
}

$assetFiles = Get-ChildItem -LiteralPath $assetsRoot -File | Sort-Object Name
$checksumLines = foreach ($asset in $assetFiles)
{
    $hash = Get-FileHash -LiteralPath $asset.FullName -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $($asset.Name)"
}
$checksumLines | Set-Content -LiteralPath (Join-Path $assetsRoot 'SHA256SUMS.txt') -Encoding ascii

Write-Host ''
Write-Host "Test-build assets created in $assetsRoot"
Get-ChildItem -LiteralPath $assetsRoot -File |
    Select-Object Name, @{ Name = 'SizeMB'; Expression = { [Math]::Round($_.Length / 1MB, 1) } }
