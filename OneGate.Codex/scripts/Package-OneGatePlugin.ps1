[CmdletBinding()]
param(
    [string]$OutputDirectory = "dist",
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.IO.Compression

$projectDirectory = Split-Path -Parent $PSScriptRoot
$pluginDirectory = Join-Path $projectDirectory "onegate"
$skillDirectory = Join-Path $pluginDirectory "skills/onegate-dapp-debug"
$manifestPath = Join-Path $pluginDirectory ".codex-plugin/plugin.json"

foreach ($requiredPath in @($pluginDirectory, $skillDirectory, $manifestPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required packaging input was not found: $requiredPath"
    }
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$version = [string]$manifest.version
if ($version -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') {
    throw "plugin.json contains an invalid release version: $version"
}

if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $resolvedOutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
} else {
    $resolvedOutputDirectory = [System.IO.Path]::GetFullPath(
        (Join-Path $projectDirectory $OutputDirectory)
    )
}
[System.IO.Directory]::CreateDirectory($resolvedOutputDirectory) | Out-Null

$skillArchivePath = Join-Path $resolvedOutputDirectory "onegate-dapp-debug-skill-$version.zip"
$pluginArchivePath = Join-Path $resolvedOutputDirectory "onegate-plugin-$version.zip"
$archivePlans = @(
    [pscustomobject]@{
        Source = $skillDirectory
        Root = "onegate-dapp-debug"
        Destination = $skillArchivePath
        RequiredEntries = @(
            "onegate-dapp-debug/SKILL.md"
            "onegate-dapp-debug/assets/reviewer-fixture/index.html"
            "onegate-dapp-debug/scripts/onegate.cmd"
            "onegate-dapp-debug/scripts/onegate.sh"
        )
    }
    [pscustomobject]@{
        Source = $pluginDirectory
        Root = "onegate"
        Destination = $pluginArchivePath
        RequiredEntries = @(
            "onegate/.codex-plugin/plugin.json"
            "onegate/assets/logo.svg"
            "onegate/skills/onegate-dapp-debug/SKILL.md"
            "onegate/skills/onegate-dapp-debug/assets/reviewer-fixture/index.html"
        )
    }
)

if (-not $Force) {
    $existingArchives = @(
        $archivePlans |
            ForEach-Object { $_.Destination } |
            Where-Object { Test-Path -LiteralPath $_ }
    )
    if ($existingArchives.Count -gt 0) {
        throw "Release archive already exists. Pass -Force to replace it: $($existingArchives -join ', ')"
    }
}

function New-DirectoryArchive {
    param(
        [Parameter(Mandatory)]
        [string]$SourceDirectory,

        [Parameter(Mandatory)]
        [string]$ArchiveRoot,

        [Parameter(Mandatory)]
        [string]$DestinationPath,

        [Parameter(Mandatory)]
        [bool]$Overwrite
    )

    $sourceFullPath = [System.IO.Path]::GetFullPath($SourceDirectory)
    $sourcePrefix = $sourceFullPath.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar
    ) + [System.IO.Path]::DirectorySeparatorChar
    $fileMode = if ($Overwrite) {
        [System.IO.FileMode]::Create
    } else {
        [System.IO.FileMode]::CreateNew
    }
    $timestamp = [System.DateTimeOffset]::new(
        1980,
        1,
        1,
        0,
        0,
        0,
        [System.TimeSpan]::Zero
    )

    $archiveStream = [System.IO.File]::Open(
        $DestinationPath,
        $fileMode,
        [System.IO.FileAccess]::ReadWrite,
        [System.IO.FileShare]::None
    )
    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $archiveStream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $true
        )
        try {
            $files = Get-ChildItem -LiteralPath $sourceFullPath -Recurse -File -Force |
                Sort-Object FullName
            foreach ($file in $files) {
                $relativePath = $file.FullName.Substring($sourcePrefix.Length).Replace('\', '/')
                $entry = $archive.CreateEntry(
                    "$ArchiveRoot/$relativePath",
                    [System.IO.Compression.CompressionLevel]::Optimal
                )
                $entry.LastWriteTime = $timestamp
                $inputStream = [System.IO.File]::OpenRead($file.FullName)
                try {
                    $entryStream = $entry.Open()
                    try {
                        $inputStream.CopyTo($entryStream)
                    } finally {
                        $entryStream.Dispose()
                    }
                } finally {
                    $inputStream.Dispose()
                }
            }
        } finally {
            $archive.Dispose()
        }
    } finally {
        $archiveStream.Dispose()
    }
}

function Assert-ArchiveEntries {
    param(
        [Parameter(Mandatory)]
        [string]$ArchivePath,

        [Parameter(Mandatory)]
        [string[]]$RequiredEntries
    )

    $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $entryNames = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::Ordinal
        )
        foreach ($entry in $archive.Entries) {
            $entryNames.Add($entry.FullName) | Out-Null
        }
        foreach ($requiredEntry in $RequiredEntries) {
            if (-not $entryNames.Contains($requiredEntry)) {
                throw "Archive is missing required entry '$requiredEntry': $ArchivePath"
            }
        }
    } finally {
        $archive.Dispose()
    }
}

foreach ($plan in $archivePlans) {
    New-DirectoryArchive `
        -SourceDirectory $plan.Source `
        -ArchiveRoot $plan.Root `
        -DestinationPath $plan.Destination `
        -Overwrite $Force.IsPresent
    Assert-ArchiveEntries `
        -ArchivePath $plan.Destination `
        -RequiredEntries $plan.RequiredEntries
}

$archivePlans | ForEach-Object {
    $file = Get-Item -LiteralPath $_.Destination
    $hash = Get-FileHash -LiteralPath $_.Destination -Algorithm SHA256
    [pscustomobject]@{
        Archive = $file.FullName
        Bytes = $file.Length
        SHA256 = $hash.Hash
    }
}
