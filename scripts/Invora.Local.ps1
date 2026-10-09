# Shared local-launcher checks. Never return or print private configuration values.
function Open-InvoraWorkspace([string]$Origin, [string]$InstallDirectory, [switch]$Browser) {
    $invoraDesktopHost = Join-Path $InstallDirectory 'desktop\Invora.Desktop.exe'
    if (-not $Browser -and (Test-Path -LiteralPath $invoraDesktopHost -PathType Leaf)) {
        Start-Process -FilePath $invoraDesktopHost -ArgumentList @('--url', $Origin) -WorkingDirectory $InstallDirectory
    } else {
        if (-not $Browser) { Write-Host 'Desktop window is not bundled in this source-only release. Opening the shop in your browser.' }
        Start-Process $Origin
    }
}
function Get-InvoraLocalEndpoint([string]$ConfigurationPath) {
    $settings = @{ INVORA_HTTP_PORT = '8080'; INVORA_BROWSER_ORIGIN = '' }
    $seen = @{}
    foreach ($line in [IO.File]::ReadAllLines($ConfigurationPath)) {
        if ($line -match '^\s*(INVORA_HTTP_PORT|INVORA_BROWSER_ORIGIN)\s*=\s*(.*)$') {
            $key = $Matches[1]
            $value = $Matches[2].Trim()
            if ($seen.ContainsKey($key)) { throw "Keep only one $key entry in .env." }
            $seen[$key] = $true
            # These two settings are literal local URLs/numbers, not secret expressions.
            if ($value -match '^"([^"]*)"\s*(?:#.*)?$' -or $value -match "^'([^']*)'\s*(?:#.*)?$") {
                $value = $Matches[1]
            } else { $value = ($value -replace '\s+#.*$', '').Trim() }
            $settings[$key] = $value
        }
    }
    foreach ($key in @('INVORA_HTTP_PORT', 'INVORA_BROWSER_ORIGIN')) {
        $override = [Environment]::GetEnvironmentVariable($key)
        if ($null -ne $override) { $settings[$key] = $override }
    }
    $port = 0
    if ($settings.INVORA_HTTP_PORT -notmatch '^[0-9]+$' -or -not [int]::TryParse($settings.INVORA_HTTP_PORT, [ref]$port) -or $port -lt 1 -or $port -gt 65535) {
        throw 'INVORA_HTTP_PORT must be a number from 1 to 65535. Correct .env or its environment override; keep the existing secrets.'
    }
    $origin = "http://127.0.0.1:$port"
    if ($settings.INVORA_BROWSER_ORIGIN.TrimEnd('/') -ne $origin) {
        throw 'For this local launcher, set INVORA_BROWSER_ORIGIN to http://127.0.0.1:<INVORA_HTTP_PORT> in .env and remove conflicting environment overrides. Use the server deployment guide for HTTPS hosting.'
    }
    return $origin
}

function Invoke-InvoraDockerCheck([string]$DockerPath, [string]$Arguments) {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $DockerPath
    $start.Arguments = $Arguments
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $start
    try {
        [void]$process.Start()
        $output = $process.StandardOutput.ReadToEndAsync()
        $errors = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(15000)) {
            $process.Kill()
            return [pscustomobject]@{ ExitCode = -1; Output = ''; TimedOut = $true }
        }
        # Drain both streams to avoid a full error buffer blocking the check. Do not expose stderr.
        [void]$errors.GetAwaiter().GetResult()
        return [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $output.GetAwaiter().GetResult().Trim(); TimedOut = $false }
    } finally { $process.Dispose() }
}
