[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$ConfigPath,
    [string]$ConnectionName,
    [ValidateRange(1, 10080)]
    [int]$RetryAfterMinutes = 60
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path (Split-Path $MyInvocation.MyCommand.Path -Parent) '..\Web.config'
}

function Get-ConfigValue {
    param([xml]$Config, [string]$Section, [string]$Name)
    $node = $Config.configuration.$Section.add | Where-Object { $_.name -eq $Name -or $_.key -eq $Name } | Select-Object -First 1
    if ($null -eq $node) { return $null }
    if ($Section -eq 'connectionStrings') { return [string]$node.connectionString }
    return [string]$node.value
}

function HtmlEncode([object]$Value) {
    return [System.Net.WebUtility]::HtmlEncode([string]$Value)
}

function Add-SqlParameter {
    param($Command, [string]$Name, [System.Data.SqlDbType]$Type, [int]$Size, $Value)
    $parameter = if ($Size -eq 0) { $Command.Parameters.Add($Name, $Type) } else { $Command.Parameters.Add($Name, $Type, $Size) }
    $parameter.Value = if ($null -eq $Value) { [DBNull]::Value } else { $Value }
}

$resolvedConfigPath = [IO.Path]::GetFullPath($ConfigPath)
if (-not (Test-Path -LiteralPath $resolvedConfigPath -PathType Leaf)) {
    throw "Configuration file was not found: $resolvedConfigPath"
}

[xml]$config = Get-Content -LiteralPath $resolvedConfigPath -Raw
if ([string]::IsNullOrWhiteSpace($ConnectionName)) {
    $ConnectionName = if ($env:COMPUTERNAME -ieq 'VALS-PC') { 'AutoDealer.dev.Development' } else { 'AutoDealer.dev.Production' }
}

$connectionString = Get-ConfigValue $config 'connectionStrings' $ConnectionName
$billingUrl = Get-ConfigValue $config 'appSettings' 'Billing:PaymentUrl'
$smtpHost = Get-ConfigValue $config 'appSettings' 'Smtp:Host'
$smtpFrom = Get-ConfigValue $config 'appSettings' 'Smtp:From'
$smtpFromName = Get-ConfigValue $config 'appSettings' 'Smtp:FromName'
$smtpUsername = Get-ConfigValue $config 'appSettings' 'Smtp:Username'
$smtpPassword = Get-ConfigValue $config 'appSettings' 'Smtp:Password'
$smtpPortText = Get-ConfigValue $config 'appSettings' 'Smtp:Port'
$smtpSslText = Get-ConfigValue $config 'appSettings' 'Smtp:EnableSsl'

if ([string]::IsNullOrWhiteSpace($connectionString)) { throw "Connection string '$ConnectionName' is not configured." }
if ([string]::IsNullOrWhiteSpace($billingUrl)) { throw "App setting 'Billing:PaymentUrl' is required." }
if ([string]::IsNullOrWhiteSpace($smtpHost) -or [string]::IsNullOrWhiteSpace($smtpFrom)) { throw 'SMTP host and sender must be configured.' }

$smtpPort = 587
if (-not [string]::IsNullOrWhiteSpace($smtpPortText)) { $smtpPort = [int]$smtpPortText }
$smtpEnableSsl = $smtpSslText -ine 'false'
$root = [IO.Path]::GetFullPath((Join-Path (Split-Path $resolvedConfigPath -Parent) '.'))
$templatePath = Join-Path $root 'Templates\Emails\trial-expired.html'
$cssPath = Join-Path $root 'Content\email.css'
$logoPath = Join-Path $root 'Content\images\autodealer-logo.png'
if (-not (Test-Path -LiteralPath $templatePath)) { throw "Email template was not found: $templatePath" }
if (-not (Test-Path -LiteralPath $cssPath)) { throw "Email stylesheet was not found: $cssPath" }
$hasLogo = Test-Path -LiteralPath $logoPath -PathType Leaf
$logoMarkup = if ($hasLogo) {
    '<div class="email-logo"><img class="email-logo-image" src="cid:autodealer-logo" width="240" alt="AutoDealer.dev"></div>'
} else {
    '<div class="email-logo email-logo-text">AutoDealer.dev</div>'
}
$template = (Get-Content -LiteralPath $templatePath -Raw).Replace('{{EMAIL_CSS}}', (Get-Content -LiteralPath $cssPath -Raw)).Replace('{{AUTODEALER_LOGO}}', $logoMarkup)

$connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
$failures = 0
try {
    $connection.Open()

    $lockCommand = $connection.CreateCommand()
    $lockCommand.CommandText = "DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=N'AutoDealer.TrialExpiration',@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=0; SELECT @result;"
    if ([int]$lockCommand.ExecuteScalar() -lt 0) { throw 'Another trial-expiration job is already running.' }

    $schemaCommand = $connection.CreateCommand()
    $schemaCommand.CommandText = "SELECT CASE WHEN COL_LENGTH('dbo.Subscriptions','TrialExpiredUtc') IS NOT NULL AND COL_LENGTH('dbo.Subscriptions','TrialExpirationNoticeSentUtc') IS NOT NULL AND OBJECT_ID('dbo.ClientEmailHistory','U') IS NOT NULL THEN 1 ELSE 0 END;"
    if ([int]$schemaCommand.ExecuteScalar() -ne 1) { throw 'Run Database/003_ClientEmailHistory.sql and Database/004_TrialExpirationAutomation.sql first.' }

    if ($PSCmdlet.ShouldProcess('expired trial subscriptions', "set status to paused and send payment notices")) {
        $pauseCommand = $connection.CreateCommand()
        $pauseCommand.CommandText = @"
UPDATE dbo.Subscriptions
SET Status='paused', TrialExpiredUtc=SYSUTCDATETIME(), UpdatedUtc=SYSUTCDATETIME()
WHERE Status='trialing' AND CurrentPeriodEndUtc<=SYSUTCDATETIME();
"@
        $pausedCount = $pauseCommand.ExecuteNonQuery()
        Write-Host "Paused $pausedCount expired trial subscription(s)."
    }

    $candidateCommand = $connection.CreateCommand()
    $candidateCommand.CommandText = @"
SELECT s.SubscriptionId,s.ClientId,c.Email,c.FirstName,c.BusinessName,p.DisplayName,s.CurrentPeriodEndUtc
FROM dbo.Subscriptions s
JOIN dbo.Clients c ON c.ClientId=s.ClientId
JOIN dbo.Plans p ON p.PlanId=s.PlanId
WHERE s.Status='paused'
  AND s.TrialExpiredUtc IS NOT NULL
  AND s.CurrentPeriodEndUtc<=SYSUTCDATETIME()
  AND s.TrialExpirationNoticeSentUtc IS NULL
  AND (s.TrialExpirationNoticeAttemptedUtc IS NULL OR s.TrialExpirationNoticeAttemptedUtc<DATEADD(minute,-@RetryMinutes,SYSUTCDATETIME()))
ORDER BY s.CurrentPeriodEndUtc,s.SubscriptionId;
"@
    Add-SqlParameter $candidateCommand '@RetryMinutes' ([System.Data.SqlDbType]::Int) 0 $RetryAfterMinutes
    $rows = New-Object System.Collections.Generic.List[object]
    $reader = $candidateCommand.ExecuteReader()
    try {
        while ($reader.Read()) {
            $rows.Add([pscustomobject]@{
                SubscriptionId = $reader.GetInt64(0); ClientId = $reader.GetInt64(1); Email = $reader.GetString(2)
                FirstName = $reader.GetString(3); BusinessName = $reader.GetString(4); PlanName = $reader.GetString(5)
                TrialEndUtc = $reader.GetDateTime(6)
            })
        }
    } finally { $reader.Close() }

    Write-Host "Found $($rows.Count) notice(s) ready for delivery."
    foreach ($row in $rows) {
        if (-not $PSCmdlet.ShouldProcess($row.Email, "send trial-expiration notice")) { continue }

        $attemptCommand = $connection.CreateCommand()
        $attemptCommand.CommandText = "UPDATE dbo.Subscriptions SET TrialExpirationNoticeAttemptedUtc=SYSUTCDATETIME(),TrialExpirationNoticeAttemptCount=TrialExpirationNoticeAttemptCount+1,TrialExpirationNoticeError=NULL WHERE SubscriptionId=@SubscriptionId AND Status='paused' AND TrialExpiredUtc IS NOT NULL AND TrialExpirationNoticeSentUtc IS NULL;"
        Add-SqlParameter $attemptCommand '@SubscriptionId' ([System.Data.SqlDbType]::BigInt) 0 $row.SubscriptionId
        if ($attemptCommand.ExecuteNonQuery() -ne 1) { continue }

        $body = $template.Replace('{{FIRST_NAME}}', (HtmlEncode $row.FirstName))
        $body = $body.Replace('{{BUSINESS_NAME}}', (HtmlEncode $row.BusinessName)).Replace('{{PLAN_NAME}}', (HtmlEncode $row.PlanName))
        $body = $body.Replace('{{BILLING_URL}}', [System.Net.WebUtility]::HtmlEncode($billingUrl)).Replace('{{TRIAL_END_UTC}}', (HtmlEncode ($row.TrialEndUtc.ToString("MMMM d, yyyy 'at' HH:mm 'UTC'"))))
        $subject = 'Your AutoDealer.dev trial has ended'

        try {
            $message = New-Object System.Net.Mail.MailMessage
            $smtp = New-Object System.Net.Mail.SmtpClient($smtpHost, $smtpPort)
            try {
                $message.From = New-Object System.Net.Mail.MailAddress($smtpFrom, $smtpFromName)
                $message.To.Add((New-Object System.Net.Mail.MailAddress($row.Email, $row.FirstName)))
                $message.Subject = $subject
                $message.SubjectEncoding = [Text.Encoding]::UTF8
                $message.Body = $body
                $message.BodyEncoding = [Text.Encoding]::UTF8
                $message.IsBodyHtml = $true
                if ($hasLogo) {
                    $htmlView = [System.Net.Mail.AlternateView]::CreateAlternateViewFromString($body, [Text.Encoding]::UTF8, 'text/html')
                    $logo = New-Object System.Net.Mail.LinkedResource($logoPath, 'image/png')
                    $logo.ContentId = 'autodealer-logo'
                    $logo.TransferEncoding = [System.Net.Mime.TransferEncoding]::Base64
                    $htmlView.LinkedResources.Add($logo)
                    $message.AlternateViews.Add($htmlView)
                }
                $smtp.EnableSsl = $smtpEnableSsl
                $smtp.UseDefaultCredentials = $false
                $smtp.Timeout = 30000
                if (-not [string]::IsNullOrWhiteSpace($smtpUsername)) {
                    $smtp.Credentials = New-Object System.Net.NetworkCredential($smtpUsername, $smtpPassword)
                }
                $smtp.Send($message)
            } finally {
                $message.Dispose()
                $smtp.Dispose()
            }

            $transaction = $connection.BeginTransaction()
            try {
                $sentCommand = $connection.CreateCommand()
                $sentCommand.Transaction = $transaction
                $sentCommand.CommandText = "UPDATE dbo.Subscriptions SET TrialExpirationNoticeSentUtc=SYSUTCDATETIME(),TrialExpirationNoticeError=NULL WHERE SubscriptionId=@SubscriptionId; INSERT dbo.ClientEmailHistory(ClientId,ToEmail,Subject,HtmlBody) VALUES(@ClientId,@Email,@Subject,@Body);"
                Add-SqlParameter $sentCommand '@SubscriptionId' ([System.Data.SqlDbType]::BigInt) 0 $row.SubscriptionId
                Add-SqlParameter $sentCommand '@ClientId' ([System.Data.SqlDbType]::BigInt) 0 $row.ClientId
                Add-SqlParameter $sentCommand '@Email' ([System.Data.SqlDbType]::NVarChar) 254 $row.Email
                Add-SqlParameter $sentCommand '@Subject' ([System.Data.SqlDbType]::NVarChar) 998 $subject
                $historyBody = $body
                if ($hasLogo) {
                    $historyLogo = 'data:image/png;base64,' + [Convert]::ToBase64String([IO.File]::ReadAllBytes($logoPath))
                    $historyBody = $historyBody.Replace('cid:autodealer-logo', $historyLogo)
                }
                Add-SqlParameter $sentCommand '@Body' ([System.Data.SqlDbType]::NVarChar) -1 $historyBody
                [void]$sentCommand.ExecuteNonQuery()
                $transaction.Commit()
            } catch { $transaction.Rollback(); throw }
            Write-Host "Sent trial-expiration notice to $($row.Email)."
        } catch {
            $failures++
            $errorText = $_.Exception.Message
            if ($errorText.Length -gt 1000) { $errorText = $errorText.Substring(0, 1000) }
            $errorCommand = $connection.CreateCommand()
            $errorCommand.CommandText = "UPDATE dbo.Subscriptions SET TrialExpirationNoticeError=@Error WHERE SubscriptionId=@SubscriptionId;"
            Add-SqlParameter $errorCommand '@Error' ([System.Data.SqlDbType]::NVarChar) 1000 $errorText
            Add-SqlParameter $errorCommand '@SubscriptionId' ([System.Data.SqlDbType]::BigInt) 0 $row.SubscriptionId
            [void]$errorCommand.ExecuteNonQuery()
            Write-Error "Notice failed for subscription $($row.SubscriptionId): $errorText" -ErrorAction Continue
        }
    }
} finally {
    if ($connection.State -eq [System.Data.ConnectionState]::Open) {
        $unlockCommand = $connection.CreateCommand()
        $unlockCommand.CommandText = "EXEC sys.sp_releaseapplock @Resource=N'AutoDealer.TrialExpiration',@LockOwner='Session';"
        try { [void]$unlockCommand.ExecuteNonQuery() } catch { }
    }
    $connection.Dispose()
}

if ($failures -gt 0) { throw "$failures trial-expiration notice(s) failed. They will be retried after $RetryAfterMinutes minutes." }
