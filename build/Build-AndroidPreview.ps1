[CmdletBinding()]
param(
    [ValidateSet('android-x64', 'android-arm64')]
    [string]$RuntimeIdentifier = 'android-x64',
    [switch]$Install,
    [string]$Device = 'emulator-5554',
    [string]$AdbPath,
    [switch]$KeepBuildDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
# Android aapt2 does not support non-ASCII project paths on Windows. Build a
# source snapshot in TEMP, keeping the desktop checkout and its outputs separate.
$buildRoot = Join-Path ([IO.Path]::GetTempPath()) ('ListenShelfAndroid-' + [Guid]::NewGuid().ToString('N'))
if ($buildRoot -match '[^\x00-\x7F]') { throw 'Android requires an ASCII-only TEMP path.' }
New-Item -ItemType Directory -Path $buildRoot | Out-Null
try {
    foreach ($file in @('Directory.Build.props', 'Directory.Packages.props', 'global.json', 'LICENSE', 'THIRD-PARTY-NOTICES.txt')) {
        Copy-Item -LiteralPath (Join-Path $repoRoot $file) -Destination $buildRoot
    }
    foreach ($project in @('ListenShelf.Core', 'ListenShelf.Application', 'ListenShelf.Infrastructure', 'ListenShelf.Playback', 'ListenShelf.Android')) {
        $sourceRoot = Join-Path $repoRoot "src/$project"
        $sourceFiles = Get-ChildItem -LiteralPath $sourceRoot -Recurse -File |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
        foreach ($source in $sourceFiles) {
            $relative = $source.FullName.Substring($repoRoot.Length).TrimStart('\', '/')
            $target = Join-Path $buildRoot $relative
            New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $source.FullName -Destination $target
        }
    }
    $branding = Join-Path $buildRoot 'src/ListenShelf.Desktop/Assets/Branding'
    New-Item -ItemType Directory -Path $branding -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot 'src/ListenShelf.Desktop/Assets/Branding/listenshelf-1024.png') -Destination $branding
    Write-Host "Android build directory: $buildRoot"
    $projectPath = Join-Path $buildRoot 'src/ListenShelf.Android/ListenShelf.Android.csproj'
    & dotnet build $projectPath -c Debug "-p:RuntimeIdentifier=$RuntimeIdentifier" -p:EmbedAssembliesIntoApk=true -p:UsedAvaloniaProducts= -nr:false -v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Android preview build failed.' }
    $apk = Join-Path $buildRoot "src/ListenShelf.Android/bin/Debug/net10.0-android/$RuntimeIdentifier/org.listenshelf.android-Signed.apk"
    if (-not (Test-Path -LiteralPath $apk)) { throw "Signed APK missing: $apk" }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($apk)
    try {
        $abi = if ($RuntimeIdentifier -eq 'android-x64') { 'x86_64' } else { 'arm64-v8a' }
        foreach ($native in @('libvlc.so', 'libc++_shared.so', 'libSkiaSharp.so', 'libe_sqlite3.so')) {
            if (-not $archive.GetEntry("lib/$abi/$native")) { throw "APK is missing its $abi native runtime: $native" }
        }
    }
    finally { $archive.Dispose() }
    $outputDirectory = Join-Path $repoRoot 'artifacts/android'
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    $outputApk = Join-Path $outputDirectory "ListenShelf-preview-$RuntimeIdentifier.apk"
    Copy-Item -LiteralPath $apk -Destination $outputApk -Force
    Write-Host "Preview APK: $outputApk"
    if ($Install) {
        if (-not $AdbPath) {
            $adbCommand = Get-Command adb -ErrorAction SilentlyContinue
            if ($adbCommand) { $AdbPath = $adbCommand.Source }
            else {
                foreach ($candidate in @((Join-Path $env:LOCALAPPDATA 'Android/Sdk/platform-tools/adb.exe'), 'C:/Program Files (x86)/Android/android-sdk/platform-tools/adb.exe')) {
                    if (Test-Path -LiteralPath $candidate) { $AdbPath = $candidate; break }
                }
            }
        }
        if (-not $AdbPath) { throw 'adb was not found. Pass -AdbPath or add Android platform-tools to PATH.' }
        & $AdbPath -s $Device install -r $outputApk
        if ($LASTEXITCODE -ne 0) { throw 'Installing the Android preview failed.' }
        & $AdbPath -s $Device shell am start -n 'org.listenshelf.android/.MainActivity'
        if ($LASTEXITCODE -ne 0) { throw 'Launching the Android preview failed.' }
    }
}
finally {
    if (-not $KeepBuildDirectory) {
        $resolved = [IO.Path]::GetFullPath($buildRoot)
        $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
        if (-not $resolved.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path $resolved -Leaf) -notmatch '^ListenShelfAndroid-[a-f0-9]{32}$') {
            throw 'Refusing to clean an unexpected Android build directory.'
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
