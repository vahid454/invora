# Local Windows launcher. It never removes database or document volumes.
[CmdletBinding()]
param([switch]$Update, [switch]$Browser)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$invoraRoot = Split-Path -Parent $PSScriptRoot
Set-Location $invoraRoot
try {
    . (Join-Path $PSScriptRoot 'Invora.Local.ps1')
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Install and start Docker Desktop with its WSL 2 / Linux containers backend first.' }
    & docker info --format '{{.OSType}}' 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Docker Desktop is not ready. Start Docker Desktop, wait until it is running, and try again.' }
    $invoraOs = & docker info --format '{{.OSType}}'
    if ($invoraOs.Trim() -ne 'linux') { throw 'Switch Docker Desktop to Linux containers, then start Invora again.' }
    & docker compose version | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Docker Compose is required. Update Docker Desktop.' }
    $invoraConfiguration = Join-Path $invoraRoot '.env'
    if (-not (Test-Path $invoraConfiguration)) {
        $invoraExistingVolumes = @(& docker volume ls --filter 'label=com.docker.compose.project=invora' --format '{{.Name}}')
        if ($LASTEXITCODE -ne 0) { throw 'Existing Docker data could not be checked.' }
        if ($invoraExistingVolumes.Count -gt 0) { throw 'Existing Invora data was found. Restore the original private .env or start from its original application folder; do not generate new credentials for existing volumes.' }
        function New-InvoraSecret([int]$length) {
            $bytes = New-Object byte[] $length
            $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
            try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
            return ([BitConverter]::ToString($bytes)).Replace('-', '').ToLowerInvariant()
        }
        $values = [ordered]@{
            POSTGRES_DB='invora'; POSTGRES_USER='invora'; POSTGRES_PASSWORD=(New-InvoraSecret 24)
            INVORA_SIGNING_KEY=(New-InvoraSecret 32); INVORA_BOOTSTRAP_KEY=(New-InvoraSecret 32)
            INVORA_BROWSER_ORIGIN='http://127.0.0.1:8080'; INVORA_ENVIRONMENT='Development'; INVORA_HTTP_PORT='8080'
        }
        $invoraPolicyPath = Join-Path $invoraRoot 'deployment\license-policy.json'
        if (Test-Path $invoraPolicyPath) {
            $invoraPolicy = Get-Content -LiteralPath $invoraPolicyPath -Raw | ConvertFrom-Json
            $values['INVORA_LICENSE_REQUIRED'] = if ($invoraPolicy.required) { 'true' } else { 'false' }
            $values['INVORA_LICENSE_PUBLIC_KEY'] = [string]$invoraPolicy.publicKey
        }
        $body = ($values.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join "`n"
        [IO.File]::WriteAllText($invoraConfiguration, $body + "`n", (New-Object Text.UTF8Encoding $false))
        $acl = New-Object Security.AccessControl.FileSecurity
        $acl.SetAccessRuleProtection($true, $false)
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
        $acl.SetOwner($identity)
        $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($identity, 'FullControl', 'Allow')))
        $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule('SYSTEM', 'FullControl', 'Allow')))
        try { Set-Acl -LiteralPath $invoraConfiguration -AclObject $acl }
        catch { Remove-Item -LiteralPath $invoraConfiguration; throw }
        Write-Host 'Created private local configuration. Keep .env on this PC; do not send it to customers or staff.'
    }
    $invoraOrigin = Get-InvoraLocalEndpoint $invoraConfiguration
    $invoraReleaseImages = @(& docker compose config --images)
    if ($LASTEXITCODE -ne 0) { throw 'Compose configuration is incomplete. Review .env without deleting existing configuration.' }
    $invoraLocalImages = @(& docker image ls --format '{{.Repository}}:{{.Tag}}')
    if ($LASTEXITCODE -ne 0) { throw 'Docker images could not be listed.' }
    $needsBuild = @($invoraReleaseImages | Where-Object { $_ -notin $invoraLocalImages }).Count -gt 0
    if ($Update -or $needsBuild) {
        Write-Host 'Building this release. The first build needs an internet connection and can take several minutes.'
        & docker compose build
        if ($LASTEXITCODE -ne 0) { throw 'Build failed. Existing records have not been removed.' }
    }
    & docker compose up -d db
    if ($LASTEXITCODE -ne 0) { throw 'Database could not start. Existing volumes have not been removed.' }
    & docker compose run --rm migrate
    if ($LASTEXITCODE -ne 0) { throw 'Migration failed. Stop here and inspect the error; do not reset or delete the database.' }
    & docker compose up -d api web
    if ($LASTEXITCODE -ne 0) { throw 'Application startup failed. Inspect docker compose logs.' }
    $invoraReady = $false
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        try {
            $ready = Invoke-WebRequest -Uri "$invoraOrigin/health/ready" -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 3
            if ($ready.StatusCode -eq 200 -and $ready.Content.Trim() -eq 'Healthy') {
                Write-Host "Invora is ready at $invoraOrigin. First time: open /setup and use INVORA_BOOTSTRAP_KEY from your private .env. No default owner password exists."
                $invoraReady = $true
                break
            }
        } catch { }
        Start-Sleep -Seconds 2
    }
    if ($invoraReady) {
        Open-InvoraWorkspace -Origin $invoraOrigin -InstallDirectory $invoraRoot -Browser:$Browser
        exit 0
    }
    throw 'Readiness failed. Run docker compose logs api web. Do not remove data volumes.'
} catch {
    Write-Error -Message $_.Exception.Message -ErrorAction Continue
    Write-Host 'Run Check-Invora.cmd for a safe diagnostic report. Keep this folder, .env and Docker volumes.'
    exit 1
}
