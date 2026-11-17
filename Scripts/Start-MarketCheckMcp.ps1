[CmdletBinding()]
param(
    [switch]$Setup,
    [string]$RuntimeDirectory = (Join-Path $env:LOCALAPPDATA 'GTX\MarketCheckMcp'),
    [string]$WebConfig,
    [switch]$Foreground
)

$ErrorActionPreference = 'Stop'
$starterPath = $PSCommandPath
if ([string]::IsNullOrWhiteSpace($starterPath)) { $starterPath = $MyInvocation.MyCommand.Path }
$starterDirectory = [IO.Path]::GetDirectoryName($starterPath)
if ([string]::IsNullOrWhiteSpace($WebConfig)) {
    $WebConfig = Join-Path ([IO.Path]::GetDirectoryName($starterDirectory)) 'Web.config'
}
$upstreamCommit = '86e645c9b927ec99ccae2a8e9cff8408e129a5b1'
$uvVersion = '0.13.9'
$uvArchiveHash = 'edd61f7f5728a56cc62455386661860bdfc7bab65872f39b0c25bdcc1d8e1beb'
$runtimePath = [IO.Path]::GetFullPath($RuntimeDirectory)
$checkoutPath = Join-Path $runtimePath 'server'
$uvPath = Join-Path $runtimePath 'uv\uv.exe'
$pythonPath = Join-Path $runtimePath 'venv\Scripts\python.exe'
$logPath = Join-Path $runtimePath 'logs'
$launcherPath = Join-Path $starterDirectory 'Start-MarketCheckMcp.py'

# Read configuration without putting the provider key into output or process arguments.
try {
    $configPath = (Resolve-Path -LiteralPath $WebConfig).ProviderPath
    $readerSettings = New-Object Xml.XmlReaderSettings
    $readerSettings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $readerSettings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create($configPath, $readerSettings)
    try {
        $config = New-Object Xml.XmlDocument
        $config.XmlResolver = $null
        $config.Load($reader)
    } finally { $reader.Dispose() }
    $keySetting = @($config.configuration.appSettings.add) | Where-Object { $_.GetAttribute('key') -eq 'MarketCheck:ApiKey' } | Select-Object -First 1
    $urlSetting = @($config.configuration.appSettings.add) | Where-Object { $_.GetAttribute('key') -eq 'MarketCheck:McpUrl' } | Select-Object -First 1
    $hasKey = $keySetting -and -not [string]::IsNullOrWhiteSpace($keySetting.GetAttribute('value'))
    $endpoint = $null
    $validUrl = $urlSetting -and [Uri]::TryCreate($urlSetting.GetAttribute('value'), [UriKind]::Absolute, [ref]$endpoint)
} catch { throw 'Read MarketCheck:ApiKey and MarketCheck:McpUrl from a valid private Web.config.' }
if (-not $hasKey) { throw 'Set MarketCheck:ApiKey in the private AutoDealer.dev configuration before starting MCP.' }
if (-not $validUrl -or $endpoint.Scheme -ne 'http' -or $endpoint.Host -notin @('127.0.0.1', 'localhost') -or
    $endpoint.AbsolutePath -ne '/mcp' -or $endpoint.Port -lt 1 -or $endpoint.UserInfo -or $endpoint.Query -or $endpoint.Fragment) {
    throw 'MarketCheck:McpUrl must be an HTTP loopback URL such as http://127.0.0.1:8765/mcp.'
}
$config = $null
$keySetting = $null
$urlSetting = $null
$healthUrl = 'http://127.0.0.1:' + $endpoint.Port + '/health'

function Test-McpHealth {
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri $healthUrl -TimeoutSec 2
        $health = $response.Content | ConvertFrom-Json
        return $response.StatusCode -eq 200 -and $health.status -eq 'healthy' -and $health.service -eq 'MarketCheck MCP Resource Server'
    } catch { return $false }
}

if (Test-McpHealth) {
    Write-Output ('MarketCheck MCP is already running on port ' + $endpoint.Port + '.')
    return
}

if ($Setup) {
    [void][IO.Directory]::CreateDirectory($runtimePath)
    [void][IO.Directory]::CreateDirectory($logPath)
    if (-not (Test-Path -LiteralPath $uvPath -PathType Leaf)) {
        $archivePath = Join-Path $runtimePath ('uv-' + $uvVersion + '.zip')
        Write-Output ('Downloading portable uv ' + $uvVersion + '...')
        Invoke-WebRequest -UseBasicParsing -Uri ('https://github.com/astral-sh/uv/releases/download/' + $uvVersion + '/uv-x86_64-pc-windows-msvc.zip') -OutFile $archivePath
        if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $uvArchiveHash) {
            throw 'The portable uv download did not match its pinned SHA-256 checksum.'
        }
        Expand-Archive -LiteralPath $archivePath -DestinationPath (Join-Path $runtimePath 'uv') -Force
    }
    if (-not (Test-Path -LiteralPath (Join-Path $checkoutPath '.git') -PathType Container)) {
        if (Test-Path -LiteralPath $checkoutPath) { throw 'The MCP server directory is not a Git checkout. Choose a new runtime directory.' }
        Write-Output 'Downloading the pinned official MarketCheck MCP server...'
        & git clone --quiet --no-checkout https://github.com/MarketcheckHub/marketcheck-api-mcp.git $checkoutPath
        if ($LASTEXITCODE -ne 0) { throw 'Could not download the official MarketCheck MCP server.' }
        & git -C $checkoutPath checkout --quiet --detach $upstreamCommit
        if ($LASTEXITCODE -ne 0) { throw 'Could not select the pinned MarketCheck MCP server revision.' }
    }
    $checkoutCommit = & git -C $checkoutPath rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $checkoutCommit.Trim() -ne $upstreamCommit) {
        throw 'The runtime checkout does not match the pinned MarketCheck revision. Choose a new runtime directory.'
    }
    $settings = @{
        UV_CACHE_DIR = (Join-Path $runtimePath 'cache')
        UV_PYTHON_INSTALL_DIR = (Join-Path $runtimePath 'python')
        UV_PROJECT_ENVIRONMENT = (Join-Path $runtimePath 'venv')
        UV_PYTHON_INSTALL_BIN = '0'
        UV_PYTHON_INSTALL_REGISTRY = '0'
        UV_PYTHON_NO_REGISTRY = '1'
        UV_NO_MODIFY_PATH = '1'
        UV_NO_CONFIG = '1'
        UV_NO_ENV_FILE = '1'
    }
    $previousEnvironment = @{}
    try {
        foreach ($name in $settings.Keys) {
            $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
            [Environment]::SetEnvironmentVariable($name, $settings[$name], 'Process')
        }
        Write-Output 'Installing the isolated Python 3.12 runtime and locked MCP dependencies...'
        $setupProcess = Start-Process -FilePath $uvPath -ArgumentList @('sync', '--frozen', '--python', '3.12') -WorkingDirectory $checkoutPath -WindowStyle Hidden -PassThru -Wait `
            -RedirectStandardOutput (Join-Path $logPath 'setup.out.log') -RedirectStandardError (Join-Path $logPath 'setup.err.log')
        if ($setupProcess.ExitCode -ne 0) { throw ('MCP dependency setup failed. See the local setup logs in ' + $logPath + '.') }
    } finally {
        foreach ($name in $previousEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process') }
    }
}

if (-not (Test-Path -LiteralPath $pythonPath -PathType Leaf) -or -not (Test-Path -LiteralPath (Join-Path $checkoutPath 'server.py') -PathType Leaf)) {
    throw 'The local MCP runtime is missing. Run this script once with -Setup, then start it normally.'
}
if (-not (Test-Path -LiteralPath $launcherPath -PathType Leaf)) { throw 'The AutoDealer.dev MCP Python launcher is missing.' }
[void][IO.Directory]::CreateDirectory($logPath)
$pythonArguments = @('-u', ('"' + $launcherPath + '"'), ('"' + $checkoutPath + '"'), '--web-config', ('"' + $configPath + '"'))
if ($Foreground) {
    & $pythonPath -u $launcherPath $checkoutPath --web-config $configPath
    if ($LASTEXITCODE -ne 0) { throw 'The MarketCheck MCP server stopped with an error.' }
    return
}
$process = Start-Process -FilePath $pythonPath -ArgumentList $pythonArguments -WorkingDirectory $checkoutPath -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $logPath 'server.out.log') -RedirectStandardError (Join-Path $logPath 'server.err.log')
$deadline = [Diagnostics.Stopwatch]::StartNew()
while ($deadline.Elapsed.TotalSeconds -lt 35) {
    if (Test-McpHealth) {
        [pscustomobject]@{ processId = $process.Id; port = $endpoint.Port; startedUtc = [DateTime]::UtcNow.ToString('o'); checkoutCommit = $upstreamCommit } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runtimePath 'server-process.json') -Encoding UTF8
        Write-Output ('MarketCheck MCP is running on port ' + $endpoint.Port + ' (PID ' + $process.Id + ').')
        return
    }
    $process.Refresh()
    if ($process.HasExited) { throw ('MarketCheck MCP stopped during startup. See local server logs in ' + $logPath + '.') }
    Start-Sleep -Milliseconds 400
}
if (-not $process.HasExited) { $process.Kill() }
throw ('MarketCheck MCP did not become ready within 35 seconds. See local server logs in ' + $logPath + '.')
