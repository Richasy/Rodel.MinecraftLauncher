param([Parameter(Mandatory)][string]$ExecutablePath)

$ErrorActionPreference = 'Stop'
$target = [IO.Path]::GetFullPath($ExecutablePath)
$processes = [Collections.Generic.List[Diagnostics.Process]]::new()
try {
    foreach ($process in [Diagnostics.Process]::GetProcessesByName([IO.Path]::GetFileNameWithoutExtension($target))) {
        $retained = $false
        try {
            # Hold the process handle before checking its image, so its PID cannot be reused.
            if ($process.HasExited) { continue }
            $null = $process.Handle
            if ($process.HasExited) { continue }
            $image = [IO.Path]::GetFullPath($process.MainModule.FileName)
            if ([string]::Equals($image, $target, [StringComparison]::OrdinalIgnoreCase)) {
                $processes.Add($process)
                $retained = $true
            }
        }
        catch {
            if (-not $process.HasExited) { throw }
        }
        finally {
            if (-not $retained) { $process.Dispose() }
        }
    }

    foreach ($process in $processes) {
        try {
            if (-not $process.HasExited) { $process.Kill() }
        }
        catch {
            if (-not $process.HasExited) { throw }
        }
    }
    foreach ($process in $processes) {
        if (-not $process.WaitForExit(10000)) { throw 'The installed App process did not terminate.' }
    }
}
finally {
    foreach ($process in $processes) { $process.Dispose() }
}
