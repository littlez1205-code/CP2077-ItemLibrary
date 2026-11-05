<#
  privacy-scan.ps1 - fail CI when a tracked file contains a real-looking Windows user folder.

  Scans every text file tracked by git for "<drive>:\Users\<name>" (either slash, any case).
  Test fixtures must use placeholder names that start with "player" (player, playerone,
  player2, player.old ...); Public / Default / All Users and placeholders such as <name>,
  %USERNAME% or an ellipsis are fine. Anything else is treated as a leaked user name.

  The file list is read from git as UTF-8 whatever the console code page is: Windows runners
  default to an OEM code page, which garbles non-ASCII names (such as the bundled user guide's)
  so they could not be opened. A tracked file that cannot be found is therefore an error, never
  skipped; only files deleted from the working tree (not committed yet) are left out.

  Usage (repo root): pwsh -File .github/scripts/privacy-scan.ps1
    exit 0 clean, 1 leak found, 2 the scan itself failed
  Tests: pwsh -File .github/scripts/privacy-scan.test.ps1
  MAINTAINERS: keep this file pure ASCII.
#>
$ErrorActionPreference = 'Stop'

# "<drive>:\Users\<name>": the name ends at a separator, quote, whitespace or placeholder bracket.
$pattern = [regex]'(?i)\b[a-z]:[\\/]+users[\\/]+([^\\/"''\s<>%`]+)'
# Matched against the text from the name on, so a folder with a space ("All Users") can be allowed.
$allowed = [regex]('(?i)^(?:player|(?:public|default|all users)(?=[\\/"''\s<>%`]|$)|\.\.\.|' + [char]0x2026 + ')')

# Runs git in $root and returns its standard output decoded as UTF-8, whatever the console code page is.
function Invoke-Git([string[]]$Arguments) {
    $info = [Diagnostics.ProcessStartInfo]::new('git')
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $info.WorkingDirectory = $root
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $process = [Diagnostics.Process]::Start($info)
    $errors = $process.StandardError.ReadToEndAsync()
    $output = $process.StandardOutput.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw ('git ' + ($Arguments -join ' ') + ' failed: ' + $errors.Result.Trim()) }
    return $output
}

function Split-Nul([string]$Text) {
    return @($Text -split "`0" | Where-Object { $_ })
}

try {
    $root = (Get-Location).ProviderPath
    $tracked = @(Split-Nul (Invoke-Git @('ls-files', '-z')))
    $deleted = @(Split-Nul (Invoke-Git @('ls-files', '-z', '--deleted')))
    $leaks = @()
    $missing = @()
    $checked = 0
    foreach ($file in $tracked) {
        if ($deleted -ccontains $file) { continue }
        $full = [IO.Path]::Combine($root, $file)
        if (Test-Path -LiteralPath $full -PathType Container) { continue }   # submodule
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) {
            $missing += $file
            continue
        }
        $bytes = [IO.File]::ReadAllBytes($full)
        $checked++
        if ([Array]::IndexOf($bytes, [byte]0) -ge 0) { continue }   # binary
        $text = [Text.Encoding]::UTF8.GetString($bytes)
        $lineNo = 0
        foreach ($line in ($text -split '\r?\n')) {
            $lineNo++
            foreach ($m in $pattern.Matches($line)) {
                $name = $m.Groups[1]
                if (-not $allowed.IsMatch($line.Substring($name.Index))) {
                    $leaks += "${file}:${lineNo}: user folder '$($name.Value)'"
                }
            }
        }
    }
}
catch {
    Write-Output ('Privacy scan error: ' + $_.Exception.Message)
    exit 2
}

if ($missing.Count -gt 0) {
    Write-Output 'Privacy scan error: tracked files could not be found, so they were not checked:'
    $missing | ForEach-Object { Write-Output "  $_" }
    exit 2
}
if ($leaks.Count -gt 0) {
    Write-Output "Privacy scan failed: real-looking Windows user folders found (use a placeholder starting with 'player')."
    $leaks | ForEach-Object { Write-Output "  $_" }
    exit 1
}
Write-Output "Privacy scan passed: $checked tracked files checked."
exit 0
