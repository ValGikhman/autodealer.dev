IF OBJECT_ID('dbo.ClientEmailHistory', 'U') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID('dbo.ClientEmailHistory')
          AND name = 'FK_ClientEmailHistory_Client'
    )
        ALTER TABLE dbo.ClientEmailHistory DROP CONSTRAINT FK_ClientEmailHistory_Client;

    ALTER TABLE dbo.ClientEmailHistory WITH CHECK ADD CONSTRAINT FK_ClientEmailHistory_Client
        FOREIGN KEY (ClientId) REFERENCES dbo.Clients(ClientId) ON DELETE CASCADE;
END;
GO

IF OBJECT_ID('dbo.DealerDemoRequests', 'U') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID('dbo.DealerDemoRequests')
          AND name = 'CK_DealerDemoRequests_Status'
    )
        ALTER TABLE dbo.DealerDemoRequests DROP CONSTRAINT CK_DealerDemoRequests_Status;

    UPDATE dbo.DealerDemoRequests
    SET Status = 'active'
    WHERE Status IN ('contacted', 'qualified');

    ALTER TABLE dbo.DealerDemoRequests WITH CHECK ADD CONSTRAINT CK_DealerDemoRequests_Status
        CHECK (Status IN ('new', 'active', 'postponed', 'closed'));
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

/* Local development uses Integrated Security, so grant to that connection's database user. */
IF @DatabaseUser IS NULL
    SET @DatabaseUser = USER_NAME();

/* dbo and the executing user cannot receive a GRANT from themselves. */
IF @DatabaseUser <> N'dbo' AND @DatabaseUser <> USER_NAME()
BEGIN
    SET @Sql = N'';

    IF OBJECT_ID('dbo.ApiUsageLog', 'U') IS NOT NULL
        SET @Sql += N'GRANT DELETE ON OBJECT::dbo.ApiUsageLog TO ' + QUOTENAME(@DatabaseUser) + N';';
    IF OBJECT_ID('dbo.ApiUsageDaily', 'U') IS NOT NULL
        SET @Sql += N'GRANT DELETE ON OBJECT::dbo.ApiUsageDaily TO ' + QUOTENAME(@DatabaseUser) + N';';
    IF OBJECT_ID('dbo.ApiKeys', 'U') IS NOT NULL
        SET @Sql += N'GRANT DELETE ON OBJECT::dbo.ApiKeys TO ' + QUOTENAME(@DatabaseUser) + N';';
    IF OBJECT_ID('dbo.PaymentProfiles', 'U') IS NOT NULL
        SET @Sql += N'GRANT DELETE ON OBJECT::dbo.PaymentProfiles TO ' + QUOTENAME(@DatabaseUser) + N';';
    IF OBJECT_ID('dbo.ClientCredentials', 'U') IS NOT NULL
        SET @Sql += N'GRANT DELETE ON OBJECT::dbo.ClientCredentials TO ' + QUOTENAME(@DatabaseUser) + N';';
    IF OBJECT_ID('dbo.Subscriptions', 'U') IS NOT NULL
        SET @Sql += N'GRANT DELETE ON OBJECT::dbo.Subscriptions TO ' + QUOTENAME(@DatabaseUser) + N';';
    IF OBJECT_ID('dbo.Clients', 'U') IS NOT NULL
        SET @Sql += N'GRANT DELETE ON OBJECT::dbo.Clients TO ' + QUOTENAME(@DatabaseUser) + N';';
    IF OBJECT_ID('dbo.DealerDemoRequests', 'U') IS NOT NULL
        SET @Sql += N'GRANT DELETE ON OBJECT::dbo.DealerDemoRequests TO ' + QUOTENAME(@DatabaseUser) + N';';

    IF LEN(@Sql) > 0
        EXEC sys.sp_executesql @Sql;
END;

IF @DatabaseUser = USER_NAME()
   AND OBJECT_ID('dbo.DealerDemoRequests', 'U') IS NOT NULL
   AND ISNULL(HAS_PERMS_BY_NAME('dbo.DealerDemoRequests', 'OBJECT', 'DELETE'), 0) = 0
    THROW 50002, 'AUTODEALER still lacks DELETE permission. Run this migration using a different database administrator login.', 1;
GO
