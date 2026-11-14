$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'CrestWindows.exe'
$crashLog = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'CrestWindowsPreview\startup.log'
$started = Get-Date

Write-Host "Starting the unpackaged Crest Windows preview from:"
Write-Host $PSScriptRoot
Write-Host ""

if (-not (Test-Path -LiteralPath $exe)) {
    Write-Warning "CrestWindows.exe is missing. Extract the complete GitHub Actions artifact ZIP."
    exit 2
}
if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'Microsoft.UI.Xaml.dll'))) {
    Write-Warning "Microsoft.UI.Xaml.dll is missing. Do not copy just the EXE; extract ALL DLLs and files into the same folder."
    exit 3
}

try {
    $process = Start-Process -FilePath $exe -WorkingDirectory $PSScriptRoot -PassThru -Wait
    Write-Host "CrestWindows.exe exited. Exit code: $($process.ExitCode)"
}
catch {
    Write-Warning "Windows could not launch the application: $($_.Exception.Message)"
}

if (Test-Path -LiteralPath $crashLog) {
    Write-Host ""
    Write-Host "Startup exception log (recent lines):"
    Get-Content -LiteralPath $crashLog -Tail 35
}

Write-Host ""
Write-Host "Recent Windows Application crash entries mentioning CrestWindows:"
try {
    $events = Get-WinEvent -FilterHashtable @{
        LogName = 'Application'
        Id = 1000, 1026
        StartTime = $started.AddSeconds(-30)
    } -MaxEvents 60 -ErrorAction Stop |
        Where-Object { $_.Message -match 'CrestWindows' } |
        Select-Object -First 3
    if ($events) {
        $events | ForEach-Object { Write-Host $_.TimeCreated; Write-Host $_.Message }
    }
    else {
        Write-Host "No recent Windows Application Error or .NET Runtime entries found."
    }
}
catch {
    Write-Host "Could not read Application events: $($_.Exception.Message)"
}
Write-Host ""
Write-Host "If Crest closes without a window, send the text above or the startup.log file."
