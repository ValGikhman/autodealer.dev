/*
  AutoDealer.dev client, billing-token, API-key, quota, and usage schema.
  Target: SQL Server 2016+.

  Run in the intended application database with a migration principal. The web
  application login should receive only the narrow table permissions needed by
  account registration, API authentication, and usage metering.

  Card numbers and CVVs are intentionally absent. Use hosted fields/checkout
  from a PCI-compliant provider and store only its opaque identifiers plus safe
  display metadata (brand and last four).
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.Clients', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Clients (
        ClientId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Clients PRIMARY KEY,
        ClientNumber varchar(32) NOT NULL,
        BusinessName nvarchar(160) NOT NULL,
        FirstName nvarchar(80) NOT NULL,
        LastName nvarchar(80) NOT NULL,
        Email nvarchar(254) NOT NULL,
        Phone nvarchar(32) NULL,
        Status varchar(20) NOT NULL CONSTRAINT DF_Clients_Status DEFAULT ('pending'),
        EmailVerifiedUtc datetime2(3) NULL,
        CreatedUtc datetime2(3) NOT NULL CONSTRAINT DF_Clients_Created DEFAULT (SYSUTCDATETIME()),
        UpdatedUtc datetime2(3) NOT NULL CONSTRAINT DF_Clients_Updated DEFAULT (SYSUTCDATETIME()),
        RowVersion rowversion NOT NULL,
        CONSTRAINT UQ_Clients_ClientNumber UNIQUE (ClientNumber),
        CONSTRAINT UQ_Clients_Email UNIQUE (Email),
        CONSTRAINT CK_Clients_Status CHECK (Status IN ('pending','active','suspended','closed'))
    );
END;
GO

IF OBJECT_ID('dbo.ClientCredentials', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ClientCredentials (
        ClientId bigint NOT NULL CONSTRAINT PK_ClientCredentials PRIMARY KEY,
        PasswordHash varbinary(64) NOT NULL,
        PasswordSalt varbinary(64) NOT NULL,
        PasswordIterations int NOT NULL,
        PasswordAlgorithm varchar(32) NOT NULL,
        PasswordChangedUtc datetime2(3) NOT NULL CONSTRAINT DF_ClientCredentials_Changed DEFAULT (SYSUTCDATETIME()),
        FailedLoginCount int NOT NULL CONSTRAINT DF_ClientCredentials_Failed DEFAULT (0),
        LockedUntilUtc datetime2(3) NULL,
        CONSTRAINT FK_ClientCredentials_Client FOREIGN KEY (ClientId) REFERENCES dbo.Clients(ClientId) ON DELETE CASCADE,
        CONSTRAINT CK_ClientCredentials_Iterations CHECK (PasswordIterations >= 100000)
    );
END;
GO

IF OBJECT_ID('dbo.Plans', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Plans (
        PlanId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Plans PRIMARY KEY,
        PlanCode varchar(32) NOT NULL CONSTRAINT UQ_Plans_Code UNIQUE,
        DisplayName nvarchar(80) NOT NULL,
        MonthlyPrice decimal(10,2) NULL,
        MonthlyRequestQuota int NOT NULL,
        MaxApiKeys smallint NOT NULL CONSTRAINT DF_Plans_MaxKeys DEFAULT (1),
        IsActive bit NOT NULL CONSTRAINT DF_Plans_Active DEFAULT (1),
        CreatedUtc datetime2(3) NOT NULL CONSTRAINT DF_Plans_Created DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT CK_Plans_Quota CHECK (MonthlyRequestQuota > 0),
        CONSTRAINT CK_Plans_MaxKeys CHECK (MaxApiKeys > 0)
    );
END;
GO

MERGE dbo.Plans WITH (HOLDLOCK) AS target
USING (VALUES
    ('STARTER',  N'Starter',  CAST(50.00 AS decimal(10,2)),   50,  1),
    ('GROWTH',   N'Growth',   CAST(150.00 AS decimal(10,2)), 150,  5),
    ('PLATFORM', N'Platform', CAST(250.00 AS decimal(10,2)), 250, 20)
) AS source (PlanCode, DisplayName, MonthlyPrice, MonthlyRequestQuota, MaxApiKeys)
ON target.PlanCode = source.PlanCode
WHEN MATCHED THEN UPDATE SET DisplayName=source.DisplayName, MonthlyPrice=source.MonthlyPrice,
    MonthlyRequestQuota=source.MonthlyRequestQuota, MaxApiKeys=source.MaxApiKeys
WHEN NOT MATCHED THEN INSERT (PlanCode,DisplayName,MonthlyPrice,MonthlyRequestQuota,MaxApiKeys)
    VALUES (source.PlanCode,source.DisplayName,source.MonthlyPrice,source.MonthlyRequestQuota,source.MaxApiKeys);
GO

IF OBJECT_ID('dbo.Subscriptions', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Subscriptions (
        SubscriptionId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Subscriptions PRIMARY KEY,
        ClientId bigint NOT NULL,
        PlanId int NOT NULL,
        Status varchar(20) NOT NULL,
        CurrentPeriodStartUtc datetime2(3) NOT NULL,
        CurrentPeriodEndUtc datetime2(3) NOT NULL,
        CancelAtPeriodEnd bit NOT NULL CONSTRAINT DF_Subscriptions_Cancel DEFAULT (0),
        ProviderSubscriptionId nvarchar(200) NULL,
        CreatedUtc datetime2(3) NOT NULL CONSTRAINT DF_Subscriptions_Created DEFAULT (SYSUTCDATETIME()),
        UpdatedUtc datetime2(3) NOT NULL CONSTRAINT DF_Subscriptions_Updated DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_Subscriptions_Client FOREIGN KEY (ClientId) REFERENCES dbo.Clients(ClientId),
        CONSTRAINT FK_Subscriptions_Plan FOREIGN KEY (PlanId) REFERENCES dbo.Plans(PlanId),
        CONSTRAINT CK_Subscriptions_Status CHECK (Status IN ('trialing','active','past_due','paused','canceled')),
        CONSTRAINT CK_Subscriptions_Period CHECK (CurrentPeriodEndUtc > CurrentPeriodStartUtc)
    );
    CREATE INDEX IX_Subscriptions_Client_Status ON dbo.Subscriptions(ClientId, Status) INCLUDE (PlanId, CurrentPeriodStartUtc, CurrentPeriodEndUtc);
END;
GO

IF OBJECT_ID('dbo.PaymentProfiles', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PaymentProfiles (
        PaymentProfileId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentProfiles PRIMARY KEY,
        ClientId bigint NOT NULL,
        Provider varchar(32) NOT NULL,
        ProviderCustomerId nvarchar(200) NULL,
        ProviderPaymentMethodId nvarchar(200) NOT NULL,
        CardBrand varchar(30) NULL,
        CardLast4 char(4) NULL,
        ExpMonth tinyint NULL,
        ExpYear smallint NULL,
        IsDefault bit NOT NULL CONSTRAINT DF_PaymentProfiles_Default DEFAULT (1),
        CreatedUtc datetime2(3) NOT NULL CONSTRAINT DF_PaymentProfiles_Created DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_PaymentProfiles_Client FOREIGN KEY (ClientId) REFERENCES dbo.Clients(ClientId) ON DELETE CASCADE,
        CONSTRAINT UQ_PaymentProfiles_ProviderMethod UNIQUE (Provider, ProviderPaymentMethodId),
        CONSTRAINT CK_PaymentProfiles_Last4 CHECK (CardLast4 IS NULL OR CardLast4 NOT LIKE '%[^0-9]%'),
        CONSTRAINT CK_PaymentProfiles_Expiry CHECK ((ExpMonth IS NULL AND ExpYear IS NULL) OR (ExpMonth BETWEEN 1 AND 12 AND ExpYear >= 2020))
    );
    CREATE INDEX IX_PaymentProfiles_Client ON dbo.PaymentProfiles(ClientId, IsDefault);
END;
GO

IF OBJECT_ID('dbo.ApiKeys', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ApiKeys (
        ApiKeyId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ApiKeys PRIMARY KEY,
        ClientId bigint NOT NULL,
        SubscriptionId bigint NOT NULL,
        KeyId varchar(64) NOT NULL,
        SecretHash binary(32) NOT NULL,
        KeyPrefix varchar(20) NOT NULL,
        Name nvarchar(80) NOT NULL,
        Scopes varchar(500) NOT NULL,
        Status varchar(20) NOT NULL CONSTRAINT DF_ApiKeys_Status DEFAULT ('active'),
        LastUsedUtc datetime2(3) NULL,
        ExpiresUtc datetime2(3) NULL,
        RevokedUtc datetime2(3) NULL,
        CreatedUtc datetime2(3) NOT NULL CONSTRAINT DF_ApiKeys_Created DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT UQ_ApiKeys_KeyId UNIQUE (KeyId),
        CONSTRAINT FK_ApiKeys_Client FOREIGN KEY (ClientId) REFERENCES dbo.Clients(ClientId),
        CONSTRAINT FK_ApiKeys_Subscription FOREIGN KEY (SubscriptionId) REFERENCES dbo.Subscriptions(SubscriptionId),
        CONSTRAINT CK_ApiKeys_Status CHECK (Status IN ('active','revoked','expired'))
    );
    CREATE INDEX IX_ApiKeys_Client_Status ON dbo.ApiKeys(ClientId, Status) INCLUDE (KeyPrefix, Name, LastUsedUtc);
END;
GO

IF OBJECT_ID('dbo.ApiUsageLog', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ApiUsageLog (
        RequestId uniqueidentifier NOT NULL CONSTRAINT PK_ApiUsageLog PRIMARY KEY,
        ApiKeyId bigint NOT NULL,
        ClientId bigint NOT NULL,
        SubscriptionId bigint NOT NULL,
        Endpoint nvarchar(300) NOT NULL,
        HttpMethod varchar(10) NOT NULL,
        StatusCode smallint NULL,
        DurationMs int NULL,
        IpAddress varchar(64) NULL,
        UserAgent nvarchar(300) NULL,
        RequestedUtc datetime2(3) NOT NULL CONSTRAINT DF_ApiUsageLog_Requested DEFAULT (SYSUTCDATETIME()),
        CompletedUtc datetime2(3) NULL,
        CONSTRAINT FK_ApiUsageLog_Key FOREIGN KEY (ApiKeyId) REFERENCES dbo.ApiKeys(ApiKeyId),
        CONSTRAINT FK_ApiUsageLog_Client FOREIGN KEY (ClientId) REFERENCES dbo.Clients(ClientId),
        CONSTRAINT FK_ApiUsageLog_Subscription FOREIGN KEY (SubscriptionId) REFERENCES dbo.Subscriptions(SubscriptionId)
    );
    CREATE INDEX IX_ApiUsageLog_Client_Date ON dbo.ApiUsageLog(ClientId, RequestedUtc DESC) INCLUDE (StatusCode, DurationMs, Endpoint);
    CREATE INDEX IX_ApiUsageLog_Key_Date ON dbo.ApiUsageLog(ApiKeyId, RequestedUtc DESC);
END;
GO

IF OBJECT_ID('dbo.ApiUsageDaily', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ApiUsageDaily (
        UsageDate date NOT NULL,
        ClientId bigint NOT NULL,
        SubscriptionId bigint NOT NULL,
        RequestCount int NOT NULL CONSTRAINT DF_ApiUsageDaily_Requests DEFAULT (0),
        ErrorCount int NOT NULL CONSTRAINT DF_ApiUsageDaily_Errors DEFAULT (0),
        TotalDurationMs bigint NOT NULL CONSTRAINT DF_ApiUsageDaily_Duration DEFAULT (0),
        CONSTRAINT PK_ApiUsageDaily PRIMARY KEY (SubscriptionId, UsageDate),
        CONSTRAINT FK_ApiUsageDaily_Client FOREIGN KEY (ClientId) REFERENCES dbo.Clients(ClientId),
        CONSTRAINT FK_ApiUsageDaily_Subscription FOREIGN KEY (SubscriptionId) REFERENCES dbo.Subscriptions(SubscriptionId),
        CONSTRAINT CK_ApiUsageDaily_Counts CHECK (RequestCount >= 0 AND ErrorCount >= 0 AND TotalDurationMs >= 0)
    );
    CREATE INDEX IX_ApiUsageDaily_Client_Date ON dbo.ApiUsageDaily(ClientId, UsageDate DESC);
END;
GO

/* Legacy reference only. API access is implemented by ApiAccessService with
   LINQ-to-SQL transactions; these procedures are intentionally not installed.
CREATE OR ALTER PROCEDURE dbo.usp_ApiAuthenticateAndBeginRequest
    @KeyId varchar(64),
    @SecretHash binary(32),
    @RequiredScope varchar(64),
    @Endpoint nvarchar(300),
    @HttpMethod varchar(10),
    @IpAddress varchar(64) = NULL,
    @UserAgent nvarchar(300) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

    DECLARE @Now datetime2(3)=SYSUTCDATETIME(), @Today date=CONVERT(date,SYSUTCDATETIME());
    DECLARE @ApiKeyId bigint, @ClientId bigint, @SubscriptionId bigint, @ClientNumber varchar(32),
            @Scopes varchar(500), @Quota int, @Usage int=0, @RequestId uniqueidentifier=NEWID();

    BEGIN TRANSACTION;
    SELECT @ApiKeyId=k.ApiKeyId, @ClientId=k.ClientId, @SubscriptionId=k.SubscriptionId,
           @ClientNumber=c.ClientNumber, @Scopes=k.Scopes, @Quota=p.MonthlyRequestQuota
    FROM dbo.ApiKeys k WITH (UPDLOCK, HOLDLOCK)
    JOIN dbo.Clients c ON c.ClientId=k.ClientId
    JOIN dbo.Subscriptions s WITH (UPDLOCK, HOLDLOCK) ON s.SubscriptionId=k.SubscriptionId
    JOIN dbo.Plans p ON p.PlanId=s.PlanId
    WHERE k.KeyId=@KeyId AND k.SecretHash=@SecretHash AND k.Status='active'
      AND (k.ExpiresUtc IS NULL OR k.ExpiresUtc>@Now) AND c.Status='active'
      AND s.Status IN ('trialing','active') AND s.CurrentPeriodEndUtc>@Now;

    IF @ApiKeyId IS NULL
    BEGIN
        COMMIT;
        SELECT 'invalid_api_key' ResultCode, CAST(NULL AS uniqueidentifier) RequestId,
               CAST(NULL AS varchar(32)) ClientNumber, CAST(NULL AS varchar(500)) Scopes,
               CAST(NULL AS int) MonthlyQuota, CAST(NULL AS int) MonthlyUsage;
        RETURN;
    END;

    IF CHARINDEX(' '+@RequiredScope+' ', ' '+@Scopes+' ') = 0
    BEGIN
        COMMIT;
        SELECT 'scope_denied' ResultCode, CAST(NULL AS uniqueidentifier) RequestId, @ClientNumber ClientNumber,
               @Scopes Scopes, @Quota MonthlyQuota, @Usage MonthlyUsage;
        RETURN;
    END;

    SELECT @Usage=ISNULL(SUM(RequestCount),0) FROM dbo.ApiUsageDaily WITH (UPDLOCK, HOLDLOCK)
    WHERE SubscriptionId=@SubscriptionId AND UsageDate>=DATEFROMPARTS(YEAR(@Today),MONTH(@Today),1)
      AND UsageDate<DATEADD(month,1,DATEFROMPARTS(YEAR(@Today),MONTH(@Today),1));

    IF @Usage>=@Quota
    BEGIN
        COMMIT;
        SELECT 'quota_exceeded' ResultCode, CAST(NULL AS uniqueidentifier) RequestId, @ClientNumber ClientNumber,
               @Scopes Scopes, @Quota MonthlyQuota, @Usage MonthlyUsage;
        RETURN;
    END;

    INSERT dbo.ApiUsageLog (RequestId,ApiKeyId,ClientId,SubscriptionId,Endpoint,HttpMethod,IpAddress,UserAgent)
    VALUES (@RequestId,@ApiKeyId,@ClientId,@SubscriptionId,@Endpoint,@HttpMethod,@IpAddress,@UserAgent);

    MERGE dbo.ApiUsageDaily WITH (HOLDLOCK) AS target
    USING (SELECT @Today UsageDate,@ClientId ClientId,@SubscriptionId SubscriptionId) source
    ON target.SubscriptionId=source.SubscriptionId AND target.UsageDate=source.UsageDate
    WHEN MATCHED THEN UPDATE SET RequestCount=target.RequestCount+1
    WHEN NOT MATCHED THEN INSERT (UsageDate,ClientId,SubscriptionId,RequestCount) VALUES (source.UsageDate,source.ClientId,source.SubscriptionId,1);

    UPDATE dbo.ApiKeys SET LastUsedUtc=@Now WHERE ApiKeyId=@ApiKeyId;
    COMMIT;
    SELECT 'ok' ResultCode,@RequestId RequestId,@ClientNumber ClientNumber,@Scopes Scopes,
           @Quota MonthlyQuota,@Usage+1 MonthlyUsage;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ApiCompleteRequest
    @RequestId uniqueidentifier,
    @StatusCode smallint,
    @DurationMs int
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @SubscriptionId bigint,@UsageDate date,@WasComplete bit=0;
    BEGIN TRANSACTION;
    SELECT @SubscriptionId=SubscriptionId,@UsageDate=CONVERT(date,RequestedUtc),@WasComplete=CASE WHEN CompletedUtc IS NULL THEN 0 ELSE 1 END
    FROM dbo.ApiUsageLog WITH (UPDLOCK,HOLDLOCK) WHERE RequestId=@RequestId;
    IF @SubscriptionId IS NOT NULL AND @WasComplete=0
    BEGIN
        UPDATE dbo.ApiUsageLog SET StatusCode=@StatusCode,DurationMs=@DurationMs,CompletedUtc=SYSUTCDATETIME() WHERE RequestId=@RequestId;
        UPDATE dbo.ApiUsageDaily SET ErrorCount=ErrorCount+CASE WHEN @StatusCode>=400 THEN 1 ELSE 0 END,
            TotalDurationMs=TotalDurationMs+@DurationMs WHERE SubscriptionId=@SubscriptionId AND UsageDate=@UsageDate;
    END;
    COMMIT;
END;
GO

Legacy procedure definitions end here.
*/
/* Optional least-privilege grants; replace AutoDealerWeb with your database user.
GRANT SELECT, INSERT ON dbo.Clients TO AutoDealerWeb;
GRANT SELECT, INSERT, UPDATE ON dbo.ClientCredentials TO AutoDealerWeb;
GRANT SELECT ON dbo.Plans TO AutoDealerWeb;
GRANT SELECT, INSERT ON dbo.Subscriptions TO AutoDealerWeb;
GRANT SELECT, INSERT, UPDATE ON dbo.ApiKeys TO AutoDealerWeb;
GRANT INSERT ON dbo.PaymentProfiles TO AutoDealerWeb;
GRANT SELECT, INSERT, UPDATE ON dbo.ApiUsageLog TO AutoDealerWeb;
GRANT SELECT, INSERT, UPDATE ON dbo.ApiUsageDaily TO AutoDealerWeb;
*/
