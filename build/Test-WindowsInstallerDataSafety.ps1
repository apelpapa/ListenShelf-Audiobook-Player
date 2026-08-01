[CmdletBinding()]
param(
    [Parameter()]
    [string] $WixSourcePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$expectedUpgradeCode = '{0C87279E-1529-4254-A0A9-111496D4E2ED}'
$protectedDirectoryIds = @(
    'LocalAppDataFolder',
    'AppDataFolder',
    'CommonAppDataFolder'
)

if ([string]::IsNullOrWhiteSpace($WixSourcePath))
{
    $WixSourcePath = Join-Path $PSScriptRoot '..\packaging\windows\ListenShelf.wxs'
}

$resolvedWixSourcePath = [IO.Path]::GetFullPath($WixSourcePath)
if (-not (Test-Path -LiteralPath $resolvedWixSourcePath -PathType Leaf))
{
    throw "The WiX source file was not found: $resolvedWixSourcePath"
}

[xml] $wixDocument = Get-Content -LiteralPath $resolvedWixSourcePath -Raw
$namespaceManager = [Xml.XmlNamespaceManager]::new($wixDocument.NameTable)
$namespaceManager.AddNamespace('w', 'http://wixtoolset.org/schemas/v4/wxs')

$package = $wixDocument.SelectSingleNode('/w:Wix/w:Package', $namespaceManager)
if ($null -eq $package)
{
    throw 'The WiX source does not contain a Package element.'
}

if ($package.UpgradeCode -ne $expectedUpgradeCode)
{
    throw "The installer UpgradeCode changed. Expected $expectedUpgradeCode but found '$($package.UpgradeCode)'."
}

if ($null -eq $package.SelectSingleNode('w:MajorUpgrade', $namespaceManager))
{
    throw 'The installer must retain MajorUpgrade support.'
}

$installFolder = $package.SelectSingleNode(
    "w:StandardDirectory[@Id='ProgramFiles64Folder']/w:Directory[@Id='INSTALLFOLDER']",
    $namespaceManager)
if ($null -eq $installFolder)
{
    throw 'INSTALLFOLDER must remain directly under ProgramFiles64Folder.'
}

foreach ($directoryId in $protectedDirectoryIds)
{
    $escapedDirectoryId = $directoryId.Replace("'", "&apos;")
    $ownedUserDataNode = $package.SelectSingleNode(
        ".//*[@Id='$escapedDirectoryId' or @Directory='$escapedDirectoryId']",
        $namespaceManager)

    if ($null -ne $ownedUserDataNode)
    {
        throw "The MSI must not own files or folders under $directoryId. User data must survive upgrades and uninstallation."
    }
}

$destructiveElementNames = @('RemoveFile', 'RemoveFolder', 'RemoveFolderEx')
foreach ($elementName in $destructiveElementNames)
{
    $element = $package.SelectSingleNode(".//*[local-name()='$elementName']", $namespaceManager)
    if ($null -ne $element)
    {
        throw "The installer contains $elementName. Review data preservation before allowing installer-authored deletion."
    }
}

Write-Host 'Windows installer data-safety checks passed: user data is outside installer ownership.'
