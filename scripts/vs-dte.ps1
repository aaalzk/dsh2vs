#Requires -Version 5.1
<#
.SYNOPSIS
  Drives a running Visual Studio instance through its DTE automation object.
  Called by dsh2vs (M4); prints one line of JSON.

.NOTES
  Runs under Windows PowerShell 5.1 on purpose: Marshal.GetActiveObject does not exist in
  .NET Core, so this is a separate process rather than code inside the bridge.
#>
param(
    [Parameter(Mandatory = $true)][string]$Action,
    [string]$Path,
    [int]$ProcessId = 0,
    [int]$TimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'

function Emit($obj) {
    $obj | ConvertTo-Json -Compress -Depth 6
}

$dte = $null
foreach ($progId in @('VisualStudio.DTE.17.0', 'VisualStudio.DTE.18.0')) {
    try {
        $dte = [Runtime.InteropServices.Marshal]::GetActiveObject($progId)
        break
    }
    catch {
        # try the next version
    }
}

if ($null -eq $dte) {
    Emit @{ ok = $false; error = 'Visual Studio is not running (no DTE object in the running object table).' }
    exit 1
}

switch ($Action.ToLowerInvariant()) {
    'status' {
        $solution = $null
        try { $solution = $dte.Solution.FullName } catch { }
        $projects = 0
        try { $projects = $dte.Solution.Projects.Count } catch { }
        Emit @{
            ok         = $true
            mode       = [string]$dte.Mode
            solution   = $solution
            projects   = $projects
            buildState = [string]$dte.Solution.SolutionBuild.BuildState
            version    = [string]$dte.Version
        }
    }

    'open' {
        if (-not (Test-Path $Path)) { Emit @{ ok = $false; error = "solution not found: $Path" }; exit 1 }
        $dte.Solution.Open($Path)
        Emit @{ ok = $true; solution = $dte.Solution.FullName; projects = $dte.Solution.Projects.Count }
    }

    'build' {
        $build = $dte.Solution.SolutionBuild
        $build.Build($true)
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        while ($build.BuildState -ne 1 -and (Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 500
        }
        $succeeded = ($build.LastBuildInfo -eq 0)
        Emit @{
            ok            = $succeeded
            lastBuildInfo = $build.LastBuildInfo
            buildState    = [string]$build.BuildState
            timedOut      = ((Get-Date) -ge $deadline)
        }
    }

    'errors' {
        $items = @()
        try {
            foreach ($e in $dte.ToolWindows.ErrorList.ErrorItems) {
                $items += @{
                    description = $e.Description
                    file        = $e.FileName
                    line        = $e.Line
                    level       = [string]$e.ErrorLevel
                }
            }
        }
        catch {
            Emit @{ ok = $false; error = "error list unreadable: $($_.Exception.Message)" }
            exit 1
        }
        Emit @{ ok = $true; count = $items.Count; items = $items }
    }

    'attach' {
        $process = $dte.Debugger.LocalProcesses | Where-Object { $_.ProcessID -eq $ProcessId } | Select-Object -First 1
        if ($null -eq $process) {
            Emit @{ ok = $false; error = "pid $ProcessId is not in Visual Studio's process list" }
            exit 1
        }
        $process.Attach()
        Emit @{ ok = $true; attached = $ProcessId; mode = [string]$dte.Mode }
    }

    'close' {
        $dte.Solution.Close($false)
        Emit @{ ok = $true; closed = $true }
    }

    default {
        Emit @{ ok = $false; error = "unknown action: $Action" }
        exit 2
    }
}
