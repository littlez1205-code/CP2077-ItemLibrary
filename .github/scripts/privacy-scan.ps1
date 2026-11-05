<#
  privacy-scan.ps1 - fail CI when a tracked file contains a real-looking Windows user folder.

  Scans every text file tracked by git for "<drive>:\Users\<name>" (either slash, any case).
  Test fixtures must use placeholder names that start with "player" (player, playerone,
  player2, player.old ...); Public / Default / All Users and placeholders such as <name>,
  %USERNAME% or an ellipsis are fine. Anything else is treated as a leaked user name.

  Usage (repo root): pwsh -File .github/scripts/privacy-scan.ps1   (exit 0 clean, 1 leak found)
  MAINTAINERS: keep this file pure ASCII.
#>
$ErrorActionPreference = 'Stop'
$pattern = '(?i)\b[a-z]:[\\/]+users[\\/]+([^\\/"''\s<>%`]+)'
$allowed = '(?i)^(player|public$|default$|all users$|\.\.\.|' + [char]0x2026 + ')'

$files = git ls-files -z | ForEach-Object { $_ -split "`0" } | Where-Object { $_ }
$leaks = @()
foreach ($file in $files) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { continue }
    $bytes = [IO.File]::ReadAllBytes($file)
    if ($bytes -contains 0) { continue }   # binary
    $text = [Text.Encoding]::UTF8.GetString($bytes)
    $lineNo = 0
    foreach ($line in ($text -split '\r?\n')) {
        $lineNo++
        foreach ($m in [regex]::Matches($line, $pattern)) {
            $name = $m.Groups[1].Value
            if ($name -notmatch $allowed) { $leaks += "${file}:${lineNo}: user folder '$name'" }
        }
    }
}

if ($leaks.Count -gt 0) {
    Write-Output "Privacy scan failed: real-looking Windows user folders found (use a placeholder starting with 'player')."
    $leaks | ForEach-Object { Write-Output "  $_" }
    exit 1
}
Write-Output "Privacy scan passed: $($files.Count) tracked files checked."
exit 0
