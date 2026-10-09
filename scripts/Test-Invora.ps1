# Read-only diagnostics. The optional report contains only predefined checks and results.
[CmdletBinding()]
param([switch]$SaveReport)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$invoraRoot = Split-Path -Parent $PSScriptRoot
Set-Location $invoraRoot
$checks = New-Object 'System.Collections.Generic.List[object]'
function Add-InvoraCheck([string]$Name, [string]$Status, [string]$Detail) {
    $checks.Add([pscustomobject]@{ check = $Name; status = $Status; detail = $Detail })
    Write-Host "[$Status] $Name - $Detail"
}
Write-Host 'Invora check: configuration and Docker are inspected; no services or records are changed.'
try {
    . (Join-Path $PSScriptRoot 'Invora.Local.ps1')
    $missing = @(@('docker-compose.yml', 'Dockerfile', 'invora-web/Dockerfile', 'Start-Invora.cmd') | Where-Object { -not (Test-Path -LiteralPath (Join-Path $invoraRoot $_) -PathType Leaf) })
    if ($missing.Count -gt 0) { Add-InvoraCheck 'Release files' 'FAIL' 'Some application files are missing. Reinstall the reviewed release into this same folder, keeping .env and data.' }
    else { Add-InvoraCheck 'Release files' 'PASS' 'Required startup files are present.' }

    $configured = Test-Path -LiteralPath (Join-Path $invoraRoot '.env') -PathType Leaf
    $origin = $null
    if ($configured) {
        try {
            $origin = Get-InvoraLocalEndpoint (Join-Path $invoraRoot '.env')
            Add-InvoraCheck 'Local address' 'PASS' 'The browser origin and local port agree.'
        } catch { Add-InvoraCheck 'Local address' 'FAIL' 'Correct INVORA_HTTP_PORT (1-65535) and its matching INVORA_BROWSER_ORIGIN in .env. Remove duplicates or conflicting environment overrides. Keep the secrets.' }
    } else { Add-InvoraCheck 'Configuration' 'WARN' 'No .env exists here. First startup creates it only if no previous shop data exists.' }

    $docker = Get-Command docker -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $docker) { Add-InvoraCheck 'Docker Desktop' 'FAIL' 'Install and start Docker Desktop with Linux containers, then run this check again.' }
    else {
        $engine = Invoke-InvoraDockerCheck $docker.Source 'info --format "{{.OSType}}"'
        if ($engine.ExitCode -ne 0) { Add-InvoraCheck 'Docker engine' 'FAIL' 'Docker did not respond successfully. Start Docker Desktop, wait for it to finish starting, then retry.' }
        elseif ($engine.Output -ne 'linux') { Add-InvoraCheck 'Docker engine' 'FAIL' 'Switch Docker Desktop to Linux containers.' }
        else {
            Add-InvoraCheck 'Docker engine' 'PASS' 'Linux container engine is ready.'
            $volumes = Invoke-InvoraDockerCheck $docker.Source 'volume ls --filter label=com.docker.compose.project=invora --format "{{.Name}}"'
            if ($volumes.ExitCode -ne 0) { Add-InvoraCheck 'Saved data' 'FAIL' 'Existing data could not be checked. Keep Docker volumes and the original configuration.' }
            elseif (-not $configured -and $volumes.Output.Length -gt 0) { Add-InvoraCheck 'Saved data' 'FAIL' 'Existing shop data was found without its configuration. Use the original folder or recover its private .env. Do not generate replacement credentials.' }
            elseif ($volumes.Output.Length -gt 0) { Add-InvoraCheck 'Saved data' 'PASS' 'Existing Invora data volumes are present. This check does not inspect records or verify backups.' }
            else { Add-InvoraCheck 'Saved data' 'WARN' 'No Invora data volumes were found in this Docker context. For an existing shop, verify the original Docker context before starting.' }
            $compose = Invoke-InvoraDockerCheck $docker.Source 'compose version'
            if ($compose.ExitCode -ne 0) { Add-InvoraCheck 'Docker Compose' 'FAIL' 'Docker Compose is unavailable. Update Docker Desktop.' }
            else {
                Add-InvoraCheck 'Docker Compose' 'PASS' 'Compose is available.'
                if ($configured -and $missing.Count -eq 0) {
                    $config = Invoke-InvoraDockerCheck $docker.Source 'compose config --quiet'
                    if ($config.ExitCode -ne 0) { Add-InvoraCheck 'Compose configuration' 'FAIL' 'Compose could not validate configuration. Check required entries against .env.example locally; do not share .env or resolved Compose output.' }
                    else {
                        Add-InvoraCheck 'Compose configuration' 'PASS' 'Required Compose settings can be resolved. Secret values are not collected.'
                        $services = Invoke-InvoraDockerCheck $docker.Source 'compose ps --status running --services'
                        if ($services.ExitCode -ne 0) { Add-InvoraCheck 'Running services' 'FAIL' 'Container status could not be read. Restart Docker Desktop and retry.' }
                        else {
                            $running = @($services.Output -split '\r?\n')
                            $stopped = @(@('db', 'api', 'web') | Where-Object { $_ -notin $running })
                            if ($stopped.Count -eq 0) { Add-InvoraCheck 'Running services' 'PASS' 'Database, API and web containers are running.' }
                            else { Add-InvoraCheck 'Running services' 'WARN' ('Not running: ' + ($stopped -join ', ') + '. Use Start Invora; preserve any startup error.') }
                        }
                    }
                }
            }
        }
    }
    if ($null -ne $origin) {
        try {
            $ready = Invoke-WebRequest -Uri "$origin/health/ready" -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 5
            if ($ready.StatusCode -ne 200 -or $ready.Content.Trim() -ne 'Healthy') { throw 'Unexpected readiness response.' }
            Add-InvoraCheck 'Application readiness' 'PASS' 'Invora reports that its database is ready. Sign in to verify the shop and its records.'
        } catch { Add-InvoraCheck 'Application readiness' 'FAIL' 'Invora is not ready at the configured local address. Run Start Invora and preserve its error. If startup reports a port conflict, choose a free port and matching browser origin.' }
    }
} catch {
    # Raw command errors may include paths or configuration. Reports intentionally contain only fixed text.
    Add-InvoraCheck 'Diagnostic execution' 'FAIL' 'A check could not complete. Retry from the installed folder; retain the startup error for local review.'
}
if ($SaveReport) {
    try {
        $directory = Join-Path $invoraRoot 'artifacts'
        [void][IO.Directory]::CreateDirectory($directory)
        $reportPath = Join-Path $directory ('invora-check-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff') + '.json')
        $report = [ordered]@{ schemaVersion = 1; checkedAtUtc = [DateTime]::UtcNow.ToString('o'); checks = @($checks.ToArray()) }
        [IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding $false))
        Write-Host "Diagnostic report saved: $reportPath"
        Write-Host 'This report excludes credentials, licence keys, logs, shop records and machine/user names. Review it before sharing.'
    } catch { Add-InvoraCheck 'Save report' 'FAIL' 'The report could not be saved. Review the checks displayed above.' }
}
if (@($checks | Where-Object { $_.status -eq 'FAIL' }).Count -gt 0) { exit 1 }
exit 0
