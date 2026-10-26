[CmdletBinding()]
param(
    [string]$ConnectionString = $env:AUTODEALER_CLEANUP_CONNECTION,
    [switch]$Execute,
    [switch]$Pause
)

$ErrorActionPreference = 'Stop'
$exitCode = 0
$connection = $null
$command = $null
$adapter = $null
try {
    if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
        throw 'Set AUTODEALER_CLEANUP_CONNECTION or supply -ConnectionString for the intended application database.'
    }
    $connection = New-Object System.Data.SqlClient.SqlConnection $ConnectionString
    $command = $connection.CreateCommand()
    $command.CommandType = [System.Data.CommandType]::StoredProcedure
    $command.CommandText = 'dbo.CleanupUnverifiedAccounts'
    $command.CommandTimeout = 120
    $null = $command.Parameters.Add('@DryRun', [System.Data.SqlDbType]::Bit)
    $command.Parameters['@DryRun'].Value = -not $Execute.IsPresent
    $null = $command.Parameters.Add('@GraceHours', [System.Data.SqlDbType]::Int)
    $command.Parameters['@GraceHours'].Value = 48
    $null = $command.Parameters.Add('@BatchSize', [System.Data.SqlDbType]::Int)
    $command.Parameters['@BatchSize'].Value = 500
    $connection.Open()
    Write-Output ('Database: {0}; server: {1}; delete enabled: {2}' -f $connection.Database, $connection.DataSource, $Execute.IsPresent)
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter $command
    $results = New-Object System.Data.DataTable
    $null = $adapter.Fill($results)
    $results | Format-Table -AutoSize
    Write-Output 'Cleanup completed successfully.'
}
catch {
    $exitCode = 1
    # Do not echo connection strings (which can contain passwords).
    Write-Error $_.Exception.Message -ErrorAction Continue
}
finally {
    if ($adapter) { $adapter.Dispose() }
    if ($command) { $command.Dispose() }
    if ($connection) { $connection.Dispose() }
    if ($Pause) { $null = Read-Host 'Press Enter to close' }
}
exit $exitCode
