# Build the v1.0 release package (D024): self-contained single-file desktop app,
# user guide (UTF-8 with BOM), zip archive and SHA-256 checksum.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File src\release\package-release.ps1
#
# Output (all under artifacts\release, which is git-ignored):
#   build\                               raw dotnet publish output
#   GameTool-CP2077-v<ver>\              files that go into the zip (no .pdb / .xml)
#   GameTool-CP2077-v<ver>-win-x64.zip
#   GameTool-CP2077-v<ver>-win-x64.zip.sha256.txt
#
# Safe to run again: it first removes only what it produced before.
# Never touches artifacts\publish or artifacts\publish-desktop.
# Exit code 0 = success, non-zero = failure.
#
# This file must stay pure ASCII: Windows PowerShell 5.1 reads BOM-less scripts
# with the system ANSI code page. Non-ASCII names are built from code points.

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

function Fail([string]$message) {
    Write-Host ('package-release: ' + $message) -ForegroundColor Red
    exit 1
}

try {
    $repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
    $props = Join-Path $repo 'src\Directory.Build.props'
    $project = Join-Path $repo 'src\GameTool.Desktop\GameTool.Desktop.csproj'
    # "Shi Yong Shuo Ming .txt" (user guide) spelled with code points to keep this file ASCII.
    $guideName = (-join @([char]0x4F7F, [char]0x7528, [char]0x8BF4, [char]0x660E)) + '.txt'
    $guideSource = Join-Path $PSScriptRoot $guideName
    # Released exe name "Sai Bo Peng Ke 2077 Wu Pin Ku .exe" (D025), also spelled with code points.
    $exeName = (-join @([char]0x8D5B, [char]0x535A, [char]0x670B, [char]0x514B)) + '2077' + (-join @([char]0x7269, [char]0x54C1, [char]0x5E93)) + '.exe'
    if (-not (Test-Path -LiteralPath $props)) { Fail ('missing ' + $props) }
    if (-not (Test-Path -LiteralPath $project)) { Fail ('missing ' + $project) }
    if (-not (Test-Path -LiteralPath $guideSource)) { Fail ('missing user guide next to this script') }

    [xml]$xml = Get-Content -LiteralPath $props -Raw
    $versionNode = $xml.SelectSingleNode('/Project/PropertyGroup/Version')
    if ($null -eq $versionNode) { Fail 'no <Version> in src\Directory.Build.props' }
    $version = $versionNode.InnerText.Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+$') { Fail ('unexpected version "' + $version + '"') }

    $releaseRoot = Join-Path $repo 'artifacts\release'
    $buildDir = Join-Path $releaseRoot 'build'
    $packageName = 'GameTool-CP2077-v' + $version
    $packageDir = Join-Path $releaseRoot $packageName
    $zipName = $packageName + '-win-x64.zip'
    $zipPath = Join-Path $releaseRoot $zipName
    $shaPath = $zipPath + '.sha256.txt'

    # 1. Clean what earlier runs produced (only inside artifacts\release).
    New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null
    if (Test-Path -LiteralPath $buildDir) { Remove-Item -LiteralPath $buildDir -Recurse -Force }
    Get-ChildItem -LiteralPath $releaseRoot -Filter 'GameTool-CP2077-v*' | ForEach-Object {
        Remove-Item -LiteralPath $_.FullName -Recurse -Force
    }

    # 2. Publish the self-contained single-file desktop app.
    Write-Host ('package-release: publishing v' + $version + ' ...')
    & dotnet publish $project -c Release -r win-x64 --self-contained `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $buildDir
    if ($LASTEXITCODE -ne 0) { Fail ('dotnet publish failed with exit code ' + $LASTEXITCODE) }
    $exe = Join-Path $buildDir 'GameTool.Desktop.exe'
    if (-not (Test-Path -LiteralPath $exe)) { Fail 'publish output has no GameTool.Desktop.exe' }

    # 3. Package folder: publish output without debug symbols and API doc xml, plus the user guide.
    New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
    Get-ChildItem -LiteralPath $buildDir -File |
        Where-Object { $_.Extension -ne '.pdb' -and $_.Extension -ne '.xml' } |
        ForEach-Object {
            $target = if ($_.Name -eq 'GameTool.Desktop.exe') { $exeName } else { $_.Name }
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $packageDir $target)
        }
    # Guide: UTF-8 with BOM and CRLF line endings whatever the checkout settings are.
    $guideText = [System.IO.File]::ReadAllText($guideSource, [System.Text.Encoding]::UTF8)
    $guideText = ($guideText -replace "`r`n", "`n") -replace "`n", "`r`n"
    [System.IO.File]::WriteAllText((Join-Path $packageDir $guideName), $guideText, (New-Object System.Text.UTF8Encoding $true))

    # 4. Zip (folder included as the top-level entry; non-ASCII names stored as UTF-8).
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $packageDir, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $true)

    # 5. Check the zip: it must contain the exe and the guide.
    $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entries = @($zip.Entries | ForEach-Object { $_.FullName -replace '\\', '/' })
    }
    finally {
        $zip.Dispose()
    }
    foreach ($required in @(($packageName + '/' + $exeName), ($packageName + '/' + $guideName))) {
        if ($entries -notcontains $required) { Fail ('zip is missing ' + $required) }
    }

    # 6. SHA-256 next to the zip: "<HASH>  <file name>".
    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    [System.IO.File]::WriteAllText($shaPath, $hash + '  ' + $zipName + "`r`n", (New-Object System.Text.UTF8Encoding $false))

    $size = (Get-Item -LiteralPath $zipPath).Length
    Write-Host ('package-release: ' + $zipPath)
    Write-Host ('package-release: size ' + $size + ' bytes (' + [math]::Round($size / 1MB, 1) + ' MB)')
    Write-Host ('package-release: SHA-256 ' + $hash)
    exit 0
}
catch {
    Fail $_.Exception.Message
}
