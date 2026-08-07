[CmdletBinding()]
param(
    [Parameter()]
    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory))
{
    $OutputDirectory = Join-Path $repoRoot 'artifacts/test-media'
}

if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue))
{
    throw 'ffmpeg is required to generate the synthetic smoke-test audiobooks.'
}

$normalizedArtifactRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$normalizedOutput = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $normalizedOutput.StartsWith(
        $normalizedArtifactRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase))
{
    throw 'Smoke-test media must be generated beneath the repository artifacts directory.'
}

if (Test-Path -LiteralPath $normalizedOutput)
{
    Remove-Item -LiteralPath $normalizedOutput -Recurse -Force
}
New-Item -ItemType Directory -Path $normalizedOutput -Force | Out-Null

$sourceM4a = Join-Path $normalizedOutput 'short-no-chapters.m4a'
$sourceM4b = Join-Path $normalizedOutput 'short-no-chapters.m4b'
$sourceMp3 = Join-Path $normalizedOutput 'short-no-chapters.mp3'
$chapteredSource = Join-Path $normalizedOutput '.chapter-source.m4a'
$chapterMetadata = Join-Path $normalizedOutput '.chapters.ffmeta'
$chapteredM4b = Join-Path $normalizedOutput 'short-with-chapters.m4b'

& ffmpeg -hide_banner -loglevel error -y -f lavfi -i 'sine=frequency=440:duration=8' -c:a aac -b:a 64k $sourceM4a
if ($LASTEXITCODE -ne 0) { throw 'M4A fixture generation failed.' }

& ffmpeg -hide_banner -loglevel error -y -f lavfi -i 'sine=frequency=554:duration=8' -c:a aac -b:a 64k -f mp4 $sourceM4b
if ($LASTEXITCODE -ne 0) { throw 'M4B fixture generation failed.' }

& ffmpeg -hide_banner -loglevel error -y -f lavfi -i 'sine=frequency=659:duration=8' -c:a libmp3lame -b:a 64k $sourceMp3
if ($LASTEXITCODE -ne 0) { throw 'MP3 fixture generation failed.' }

& ffmpeg -hide_banner -loglevel error -y -f lavfi -i 'sine=frequency=330:duration=12' -c:a aac -b:a 64k $chapteredSource
if ($LASTEXITCODE -ne 0) { throw 'Chaptered source generation failed.' }

$metadata = @'
;FFMETADATA1
[CHAPTER]
TIMEBASE=1/1000
START=0
END=4000
title=Opening
[CHAPTER]
TIMEBASE=1/1000
START=4000
END=8000
title=Middle
[CHAPTER]
TIMEBASE=1/1000
START=8000
END=12000
title=Ending
'@
[IO.File]::WriteAllText(
    $chapterMetadata,
    $metadata,
    [Text.UTF8Encoding]::new($false))

& ffmpeg -hide_banner -loglevel error -y -i $chapteredSource -i $chapterMetadata -map_metadata 1 -map_chapters 1 -codec copy $chapteredM4b
if ($LASTEXITCODE -ne 0) { throw 'Chaptered M4B fixture generation failed.' }

Copy-Item -LiteralPath $sourceM4b -Destination (Join-Path $normalizedOutput 'Café — 第1章.m4b')
Remove-Item -LiteralPath $chapteredSource, $chapterMetadata -Force

$manifestLines = Get-ChildItem -LiteralPath $normalizedOutput -File |
    Sort-Object Name |
    ForEach-Object {
        $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
        "$($hash.Hash.ToLowerInvariant())  $($_.Name)"
    }
[IO.File]::WriteAllLines(
    (Join-Path $normalizedOutput 'SHA256SUMS.txt'),
    $manifestLines,
    [Text.UTF8Encoding]::new($false))

Write-Host "Synthetic smoke-test media created in $normalizedOutput"
Get-ChildItem -LiteralPath $normalizedOutput -File | Select-Object Name, Length
