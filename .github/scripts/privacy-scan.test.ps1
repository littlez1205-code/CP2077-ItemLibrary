<#
  privacy-scan.test.ps1 - runs privacy-scan.ps1 against throwaway git repositories.

  Covers what the scan must get right on a Windows runner, where the console code page is not
  UTF-8: a leak inside a file with a non-ASCII (Chinese) name, like the bundled user guide, must
  still be reported; placeholder folders (player*, Public, Default, All Users, <name>,
  %USERPROFILE%, an ellipsis) must pass; a tracked file deleted from the working tree is not an
  error; binary files are skipped.

  Usage (repo root): pwsh -File .github/scripts/privacy-scan.test.ps1   (exit 0 all passed, 1 a case failed)
  MAINTAINERS: keep this file pure ASCII; non-ASCII text is built from code points.
#>
$ErrorActionPreference = 'Stop'
$scan = Join-Path $PSScriptRoot 'privacy-scan.ps1'
$pwsh = (Get-Process -Id $PID).Path
# "Shuo Ming .txt": a non-ASCII file name like the user guide's, built from code points.
$guideName = (-join @([char]0x8BF4, [char]0x660E)) + '.txt'
$ellipsis = [string][char]0x2026
$utf8 = [Text.UTF8Encoding]::new($false)
$failed = 0

# Creates a git repository holding $Files (name -> text or byte[]), stages everything, deletes the
# names in $Delete from the working tree, runs the scan there in a separate pwsh (as CI does) and
# checks its exit code and, when given, that its output mentions $Expect.
function Invoke-Case {
    param([string]$Name, [hashtable]$Files, [int]$Expected, [string]$Expect = '', [string[]]$Delete = @())
    $dir = Join-Path ([IO.Path]::GetTempPath()) ('privacy-scan-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    try {
        foreach ($file in $Files.Keys) {
            $content = $Files[$file]
            $path = Join-Path $dir $file
            if ($content -is [byte[]]) { [IO.File]::WriteAllBytes($path, $content) }
            else { [IO.File]::WriteAllText($path, [string]$content, $utf8) }
        }
        Push-Location -LiteralPath $dir
        try {
            git -c init.defaultBranch=main init -q
            git -c core.autocrlf=false add -A
            if ($LASTEXITCODE -ne 0) { throw 'git add failed' }
            foreach ($file in $Delete) { Remove-Item -LiteralPath (Join-Path $dir $file) }
            $output = (& $pwsh -NoProfile -NonInteractive -File $scan 2>&1 | Out-String).TrimEnd()
            $exit = $LASTEXITCODE
        }
        finally {
            Pop-Location
        }
        if ($exit -eq $Expected -and ($Expect -eq '' -or $output.Contains($Expect))) {
            Write-Output ('PASS  ' + $Name)
        }
        else {
            Write-Output ('FAIL  ' + $Name + ' (exit ' + $exit + ', expected ' + $Expected + ')')
            foreach ($line in ($output -split '\r?\n')) { Write-Output ('      ' + $line) }
            $script:failed++
        }
    }
    finally {
        Remove-Item -LiteralPath $dir -Recurse -Force
    }
}

$placeholders = @(
    'C:\Users\player\Saved Games\CD Projekt Red\Cyberpunk 2077',
    'c:/users/PlayerOne/AppData/Local/GameTool',
    'var path = "C:\\Users\\player2\\Saved Games";',
    'C:\Users\Public\Documents',
    'C:\Users\Default\AppData',
    'C:\Users\All Users\Microsoft',
    '%USERPROFILE%\Saved Games',
    'C:\Users\<name>\Saved Games',
    'C:\Users\...\Saved Games',
    ('C:\Users\' + $ellipsis + '\Saved Games')
) -join "`n"

Invoke-Case -Name 'placeholder folders pass, also in a non-ASCII-named file' -Expected 0 `
    -Files @{ 'notes.md' = $placeholders; $guideName = $placeholders }
Invoke-Case -Name 'a leak in an ASCII-named file is reported' -Expected 1 -Expect 'alice' `
    -Files @{ 'notes.md' = 'Saves are in C:\Users\alice\Saved Games' }
Invoke-Case -Name 'a leak in a non-ASCII-named file is reported' -Expected 1 -Expect 'alice' `
    -Files @{ 'notes.md' = 'nothing to see'; $guideName = 'Saves are in C:\Users\alice\Saved Games' }
Invoke-Case -Name 'a tracked file deleted from the working tree is not an error' -Expected 0 `
    -Files @{ 'notes.md' = 'nothing to see'; 'gone.txt' = 'nothing to see' } -Delete @('gone.txt')
Invoke-Case -Name 'binary files are skipped' -Expected 0 `
    -Files @{ 'notes.md' = 'nothing to see'; 'blob.bin' = $utf8.GetBytes("`0C:\Users\alice\x") }

if ($failed -gt 0) {
    Write-Output ($failed.ToString() + ' case(s) failed.')
    exit 1
}
Write-Output 'All privacy scan cases passed.'
exit 0
