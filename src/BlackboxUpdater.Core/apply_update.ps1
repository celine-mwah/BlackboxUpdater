param(
    [int]$GamePid,
    [long]$GameStartTicks,
    [string]$InstallDir,
    [string]$StageDir,
    [string]$BackupDir,
    [int]$RetryCount = 15
)
$ErrorActionPreference = 'Stop'
$ResultFile = Join-Path (Split-Path $StageDir -Parent) 'last_result.txt'
$FilesDir = Join-Path $StageDir 'files'
$entries = New-Object System.Collections.ArrayList

function Move-WithRetry($from, $to) {
    for ($i = 1; ; $i++) {
        try { Move-Item -LiteralPath $from -Destination $to -Force; return }
        catch { if ($i -ge $RetryCount) { throw }; Start-Sleep -Seconds 1 }
    }
}

function Test-RelPath($p) {
    return ($p -match '^[A-Za-z0-9_./ -]+$') -and ($p -notmatch '(^|/)\.\.?(/|$)') -and (-not $p.StartsWith('/')) -and ($p -notmatch ':')
}

try {
    $p = if ($GamePid -gt 0) { Get-Process -Id $GamePid -ErrorAction SilentlyContinue }
    if ($p -and ($GameStartTicks -eq 0 -or $p.StartTime.ToFileTimeUtc() -eq $GameStartTicks)) { $p.WaitForExit() }
    Start-Sleep -Seconds 2

    $lines = @(Get-Content -LiteralPath (Join-Path $StageDir 'plan.txt') -Encoding UTF8)
    if ($lines.Count -lt 2) { throw 'plan.txt is empty' }
    $version = $lines[0]
    $plan = $lines | Select-Object -Skip 1

    if (Test-Path -LiteralPath $BackupDir) { Remove-Item -LiteralPath $BackupDir -Recurse -Force }

    foreach ($rel in $plan) {
        if (-not (Test-RelPath $rel)) { throw "bad path in plan: $rel" }
        $win = $rel.Replace('/', '\')
        $src = Join-Path $FilesDir $win
        $dest = Join-Path $InstallDir $win
        $bak = Join-Path $BackupDir $win
        if (-not (Test-Path -LiteralPath $src -PathType Leaf)) { throw "staged file missing: $rel" }
        New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null

        $e = [pscustomobject]@{ Dest = $dest; Bak = $bak; Had = (Test-Path -LiteralPath $dest -PathType Leaf); Moved = $false; Placed = $false }
        [void]$entries.Add($e)
        if ($e.Had) {
            New-Item -ItemType Directory -Force -Path (Split-Path $bak -Parent) | Out-Null
            Move-WithRetry $dest $bak
            $e.Moved = $true
        }
        Move-WithRetry $src $dest
        $e.Placed = $true
    }

    Remove-Item -LiteralPath $StageDir -Recurse -Force
    Set-Content -LiteralPath $ResultFile -Value ('OK {0} {1}' -f $version, (Get-Date -Format s)) -Encoding UTF8
}
catch {
    $err = $_.Exception.Message
    for ($i = $entries.Count - 1; $i -ge 0; $i--) {
        $e = $entries[$i]
        try {
            if ($e.Placed) { Remove-Item -LiteralPath $e.Dest -Force }
            if ($e.Moved) { Move-Item -LiteralPath $e.Bak -Destination $e.Dest -Force }
        }
        catch { $err += " | rollback failed for $($e.Dest): $($_.Exception.Message)" }
    }
    Set-Content -LiteralPath $ResultFile -Value ('FAILED {0} {1}' -f (Get-Date -Format s), $err) -Encoding UTF8
    exit 1
}
