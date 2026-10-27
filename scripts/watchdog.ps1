#Requires -Version 5.1
<#
.SYNOPSIS
  Watchdog: starts dsh2vs when /healthz stops answering. Intended for a scheduled task every few minutes.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Workspace,
    [int]$Port = 8790
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dll = Join-Path $root 'src\Dsh2Vs.Server\bin\Debug\net9.0\Dsh2Vs.Server.dll'
$logDir = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

function Test-Health {
    try {
        $r = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/healthz" -UseBasicParsing -TimeoutSec 3
        return $r.StatusCode -eq 200
    }
    catch {
        return $false
    }
}

if (Test-Health) {
    exit 0
}

if (-not (Test-Path $dll)) {
    "[watchdog] server build missing: $dll" | Add-Content (Join-Path $logDir 'watchdog.log')
    exit 1
}

Start-Process -FilePath 'dotnet' `
    -ArgumentList "`"$dll`" --Dsh2Vs:WorkspaceRoot=`"$Workspace`" --urls http://127.0.0.1:$Port" `
    -WindowStyle Hidden | Out-Null

Start-Sleep -Seconds 3
if (Test-Health) {
    "[watchdog] restarted at $(Get-Date -Format o)" | Add-Content (Join-Path $logDir 'watchdog.log')
    exit 0
}

"[watchdog] restart failed at $(Get-Date -Format o)" | Add-Content (Join-Path $logDir 'watchdog.log')
exit 2
