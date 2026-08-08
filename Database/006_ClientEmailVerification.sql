/* One-time email verification for new customer workspaces. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.ClientEmailVerifications', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ClientEmailVerifications (
        VerificationId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ClientEmailVerifications PRIMARY KEY,
        ClientId bigint NOT NULL,
        TokenHash char(64) NOT NULL,
        CreatedByAdmin bit NOT NULL CONSTRAINT DF_ClientEmailVerifications_Admin DEFAULT (0),
        ExpiresUtc datetime2(3) NOT NULL,
        CreatedUtc datetime2(3) NOT NULL CONSTRAINT DF_ClientEmailVerifications_Created DEFAULT (SYSUTCDATETIME()),
        UsedUtc datetime2(3) NULL,
        CredentialsSentUtc datetime2(3) NULL,
        CONSTRAINT UQ_ClientEmailVerifications_TokenHash UNIQUE (TokenHash),
        CONSTRAINT FK_ClientEmailVerifications_Client FOREIGN KEY (ClientId)
            REFERENCES dbo.Clients(ClientId) ON DELETE CASCADE
    );

    CREATE INDEX IX_ClientEmailVerifications_Client
        ON dbo.ClientEmailVerifications(ClientId, CreatedUtc DESC);
END;
GO

DECLARE @ProductionLogin sysname = N'AUTODEALER';
DECLARE @ProductionLoginSid varbinary(85) = SUSER_SID(@ProductionLogin);
DECLARE @DatabaseUser sysname;
DECLARE @Sql nvarchar(max);

SELECT TOP (1) @DatabaseUser = name
FROM sys.database_principals
WHERE sid = @ProductionLoginSid
ORDER BY CASE WHEN name = N'dbo' THEN 0 ELSE 1 END;

IF @DatabaseUser IS NULL
    SET @DatabaseUser = USER_NAME();

IF @DatabaseUser <> N'dbo' AND @DatabaseUser <> USER_NAME()
BEGIN
    SET @Sql = N'GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.ClientEmailVerifications TO '
        + QUOTENAME(@DatabaseUser) + N';'
        + N'GRANT SELECT, UPDATE ON OBJECT::dbo.Clients TO ' + QUOTENAME(@DatabaseUser) + N';'
        + N'GRANT SELECT, UPDATE ON OBJECT::dbo.Subscriptions TO ' + QUOTENAME(@DatabaseUser) + N';'
        + N'GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.ApiKeys TO ' + QUOTENAME(@DatabaseUser) + N';';
    EXEC sys.sp_executesql @Sql;
END;
GO
