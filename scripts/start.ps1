#Requires -Version 5.1
<#
.SYNOPSIS
  Runs dsh2vs in this console (Ctrl+C to stop). For an always-on service, register the
  command printed at the end with Task Scheduler.

.EXAMPLE
  .\scripts\start.ps1 -Workspace D:\Projects\MyCppRepo
  .\scripts\start.ps1 -Workspace D:\Projects\MyCppRepo -Port 8790 -Build
#>
[CmdletBinding()]
param(
    [string]$Workspace = (Get-Location).Path,
    [int]$Port = 8790,
    [switch]$Build
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\Dsh2Vs.Server\Dsh2Vs.Server.csproj'
$dll = Join-Path $root 'src\Dsh2Vs.Server\bin\Debug\net9.0\Dsh2Vs.Server.dll'

if (-not (Test-Path $Workspace)) { throw "Workspace not found: $Workspace" }
$Workspace = (Resolve-Path $Workspace).Path

if ($Build -or -not (Test-Path $dll)) {
    Write-Host 'Building dsh2vs...' -ForegroundColor Cyan
    dotnet build $project -v:q --nologo | Out-Null
}

Write-Host ''
Write-Host "dsh2vs  workspace : $Workspace"
Write-Host "        MCP       : http://127.0.0.1:$Port/mcp"
Write-Host "        health    : http://127.0.0.1:$Port/healthz"
Write-Host "        DSH       : serverName 'vs', transport streamable-http"
Write-Host ''
Write-Host 'Task Scheduler action (always-on):' -ForegroundColor DarkGray
Write-Host ("  dotnet `"{0}`" --Dsh2Vs:WorkspaceRoot=`"{1}`" --urls http://127.0.0.1:{2}" -f $dll, $Workspace, $Port) -ForegroundColor DarkGray
Write-Host ''

dotnet run --project $project --no-build -- "--Dsh2Vs:WorkspaceRoot=$Workspace" --urls "http://127.0.0.1:$Port"
