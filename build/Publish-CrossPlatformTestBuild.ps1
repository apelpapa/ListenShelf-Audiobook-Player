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
    Move-Item -LiteralPath (Join-Path $packageRoot 'ListenShelf') -Destination (Join-Path $packageRoot 'ListenShelf.bin')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'packaging/linux/listenshelf-launcher.sh') -Destination (Join-Path $packageRoot 'ListenShelf')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'packaging/linux/TEST-BUILD-README.txt') -Destination $packageRoot
    Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/CROSS_PLATFORM_TEST_BUILDS.md') -Destination (Join-Path $packageRoot 'TESTING-INSTRUCTIONS.md')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'packaging/linux/listenshelf.desktop') -Destination $packageRoot
    Copy-Item -LiteralPath (Join-Path $repoRoot 'src/ListenShelf.Desktop/Assets/Branding/listenshelf-1024.png') -Destination (Join-Path $packageRoot 'listenshelf.png')
    & chmod '+x' (Join-Path $packageRoot 'ListenShelf') (Join-Path $packageRoot 'ListenShelf.bin')
    if ($LASTEXITCODE -ne 0)
    {
        throw "chmod failed with exit code $LASTEXITCODE."
    }

    & bash `
        (Join-Path $repoRoot 'build/Bundle-LinuxLibVlc.sh') `
        $packageRoot `
        (Join-Path $packageRoot 'libvlc')
    if ($LASTEXITCODE -ne 0)
    {
        throw "Bundling the private Linux LibVLC runtime failed with exit code $LASTEXITCODE."
    }

    & (Join-Path $packageRoot 'ListenShelf') '--verify-native-runtime'
    if ($LASTEXITCODE -ne 0)
    {
        throw "The packaged Linux LibVLC runtime probe failed with exit code $LASTEXITCODE."
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
    $frameworksRoot = Join-Path $contentsRoot 'Frameworks'
    New-Item -ItemType Directory -Path $macOsRoot, $resourcesRoot, $frameworksRoot -Force | Out-Null
    Copy-Item -Path (Join-Path $publishRoot '*') -Destination $macOsRoot -Recurse
    Move-Item -LiteralPath (Join-Path $macOsRoot 'ListenShelf') -Destination (Join-Path $macOsRoot 'ListenShelf.bin')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'packaging/macos/listenshelf-launcher.sh') -Destination (Join-Path $macOsRoot 'ListenShelf')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'src/ListenShelf.Desktop/Assets/Branding/listenshelf.icns') -Destination $resourcesRoot
    Copy-Item -LiteralPath (Join-Path $repoRoot 'packaging/macos/TEST-BUILD-README.txt') -Destination $packageRoot
    Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/CROSS_PLATFORM_TEST_BUILDS.md') -Destination (Join-Path $packageRoot 'TESTING-INSTRUCTIONS.md')

    $vlcAppPath = if ([string]::IsNullOrWhiteSpace($env:LISTENSHELF_PACKAGING_VLC_APP))
    {
        '/Applications/VLC.app'
    }
    else
    {
        $env:LISTENSHELF_PACKAGING_VLC_APP
    }
    $vlcContentsRoot = Join-Path $vlcAppPath 'Contents'
    $installedVlcRoot = Join-Path $vlcContentsRoot 'MacOS'
    $installedVlcLib = Join-Path $installedVlcRoot 'lib'
    $installedVlcPlugins = Join-Path $installedVlcRoot 'plugins'
    if (-not (Test-Path -LiteralPath (Join-Path $installedVlcLib 'libvlc.dylib')) -or
        -not (Test-Path -LiteralPath $installedVlcPlugins))
    {
        throw "A complete architecture-compatible VLC.app is required at '$vlcAppPath' so its LibVLC runtime can be embedded."
    }

    $bundledVlcRoot = Join-Path $frameworksRoot 'libvlc'
    $bundledVlcLib = Join-Path $bundledVlcRoot 'lib'
    $bundledVlcPlugins = Join-Path $bundledVlcRoot 'plugins'
    & ditto $installedVlcLib $bundledVlcLib
    if ($LASTEXITCODE -ne 0)
    {
        throw "Copying the macOS LibVLC libraries failed with exit code $LASTEXITCODE."
    }
    & ditto $installedVlcPlugins $bundledVlcPlugins
    if ($LASTEXITCODE -ne 0)
    {
        throw "Copying the macOS LibVLC plugins failed with exit code $LASTEXITCODE."
    }
    Get-ChildItem -LiteralPath $bundledVlcPlugins -Recurse -File -Filter 'plugins.dat' |
        Remove-Item -Force

    $expectedArchitecture = if ($RuntimeIdentifier -eq 'osx-arm64') { 'arm64' } else { 'x86_64' }
    $bundledLibVlcPath = Join-Path $bundledVlcLib 'libvlc.dylib'
    $bundledArchitectures = (& lipo '-archs' $bundledLibVlcPath).Trim().Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
    if ($LASTEXITCODE -ne 0 -or $expectedArchitecture -notin $bundledArchitectures)
    {
        throw "The bundled LibVLC runtime does not contain the required $expectedArchitecture architecture. Found: $($bundledArchitectures -join ', ')."
    }

    $pluginCount = @(Get-ChildItem -LiteralPath $bundledVlcPlugins -Recurse -File -Filter '*.dylib').Count
    if ($pluginCount -lt 20)
    {
        throw "Only $pluginCount macOS LibVLC plugins were bundled; refusing an incomplete package."
    }
    foreach ($requiredPlugin in 'libmp4_plugin.dylib', 'libavcodec_plugin.dylib')
    {
        if (-not (Get-ChildItem -LiteralPath $bundledVlcPlugins -Recurse -File -Filter $requiredPlugin | Select-Object -First 1))
        {
            throw "Required playback plugin $requiredPlugin was not bundled."
        }
    }

    $bundledLicenseRoot = Join-Path $bundledVlcRoot 'licenses'
    New-Item -ItemType Directory -Path $bundledLicenseRoot -Force | Out-Null
    $licenseCandidates = @(Get-ChildItem -LiteralPath $vlcContentsRoot -Recurse -File |
        Where-Object { $_.Name -match '^(AUTHORS|COPYING|COPYRIGHT|LICENSE)(\..*)?$' })
    foreach ($licenseFile in $licenseCandidates)
    {
        $relativeLicensePath = [IO.Path]::GetRelativePath(
            $vlcContentsRoot,
            $licenseFile.FullName)
        $licenseName = $relativeLicensePath.Replace('/', '-').Replace('\', '-')
        Copy-Item -LiteralPath $licenseFile.FullName -Destination (Join-Path $bundledLicenseRoot $licenseName)
    }

    $vlcVersion = (& plutil '-extract' 'CFBundleShortVersionString' 'raw' (Join-Path $vlcContentsRoot 'Info.plist')).Trim()
    if ($LASTEXITCODE -ne 0)
    {
        throw "Reading the bundled VLC version failed with exit code $LASTEXITCODE."
    }
    $runtimeManifest = @(
        'ListenShelf private LibVLC runtime',
        'Source: Official VideoLAN VLC.app',
        "VLC version: $vlcVersion",
        "Architecture: $expectedArchitecture",
        "Plugins: $pluginCount",
        "Source license files: $($licenseCandidates.Count)"
    ) -join [Environment]::NewLine
    [IO.File]::WriteAllText(
        (Join-Path $bundledVlcRoot 'BUNDLED-RUNTIME.txt'),
        $runtimeManifest + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))

    $plistTemplate = Get-Content -LiteralPath (Join-Path $repoRoot 'packaging/macos/Info.plist') -Raw
    $plist = $plistTemplate.Replace('__NUMERIC_VERSION__', $numericVersion)
    [IO.File]::WriteAllText(
        (Join-Path $contentsRoot 'Info.plist'),
        $plist,
        [Text.UTF8Encoding]::new($false))
    & chmod '+x' (Join-Path $macOsRoot 'ListenShelf') (Join-Path $macOsRoot 'ListenShelf.bin')
    if ($LASTEXITCODE -ne 0)
    {
        throw "chmod failed with exit code $LASTEXITCODE."
    }

    & (Join-Path $macOsRoot 'ListenShelf') '--verify-native-runtime'
    if ($LASTEXITCODE -ne 0)
    {
        throw "The packaged macOS LibVLC runtime probe failed with exit code $LASTEXITCODE."
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
