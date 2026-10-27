#Requires -Version 5.1
<#
.SYNOPSIS
  Registers dsh2vs as a logon task plus a 5-minute watchdog task, so a Windows Update, a crashed
  session or an accidental click cannot leave the bridge offline.

.EXAMPLE
  .\scripts\install-service.ps1 -Workspace D:\Projects\MyCppRepo
  .\scripts\install-service.ps1 -Status
  .\scripts\install-service.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [string]$Workspace = (Get-Location).Path,
    [int]$Port = 8790,
    [switch]$Uninstall,
    [switch]$Status
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$serverTask = 'dsh2vs'
$watchTask = 'dsh2vs-watchdog'
$dll = Join-Path $root 'src\Dsh2Vs.Server\bin\Debug\net9.0\Dsh2Vs.Server.dll'

if ($Status) {
    foreach ($name in @($serverTask, $watchTask)) {
        $task = Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
        if ($task) {
            $info = Get-ScheduledTaskInfo -TaskName $name -ErrorAction SilentlyContinue
            "$name : $($task.State)  last=$($info.LastRunTime)  result=$($info.LastTaskResult)"
        }
        else {
            "$name : not registered"
        }
    }
    return
}

if ($Uninstall) {
    foreach ($name in @($serverTask, $watchTask)) {
        Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue
        "removed $name"
    }
    return
}

if (-not (Test-Path $Workspace)) { throw "Workspace not found: $Workspace" }
$Workspace = (Resolve-Path $Workspace).Path
if (-not (Test-Path $dll)) { throw "Build the solution first: dotnet build dsh2vs.sln" }

$user = "$env:USERDOMAIN\$env:USERNAME"

$serverAction = New-ScheduledTaskAction -Execute 'dotnet' `
    -Argument "`"$dll`" --Dsh2Vs:WorkspaceRoot=`"$Workspace`" --urls http://127.0.0.1:$Port"
$serverTrigger = New-ScheduledTaskTrigger -AtLogOn -User $user
$serverSettings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -StartWhenAvailable -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero)
Register-ScheduledTask -TaskName $serverTask -Action $serverAction -Trigger $serverTrigger `
    -Settings $serverSettings -Description 'dsh2vs bridge for DSH (Visual Studio C++ build/debug)' -Force | Out-Null

$watchAction = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$PSScriptRoot\watchdog.ps1`" -Workspace `"$Workspace`" -Port $Port"
$watchTrigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) `
    -RepetitionInterval (New-TimeSpan -Minutes 5) -RepetitionDuration (New-TimeSpan -Days 3650)
Register-ScheduledTask -TaskName $watchTask -Action $watchAction -Trigger $watchTrigger `
    -Settings (New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Minutes 5)) `
    -Description 'dsh2vs watchdog: restarts the bridge when /healthz stops answering' -Force | Out-Null

Write-Host "registered: $serverTask (at logon), $watchTask (every 5 min)" -ForegroundColor Green
Write-Host "workspace : $Workspace"
Write-Host "endpoint  : http://127.0.0.1:$Port/mcp"
