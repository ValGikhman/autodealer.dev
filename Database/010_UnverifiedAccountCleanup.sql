/* Run in the APPLICATION database. Installs a procedure; deletes no customers.
   Preview: EXEC dbo.CleanupUnverifiedAccounts;
   Delete:  EXEC dbo.CleanupUnverifiedAccounts @DryRun=0;
   Requires existing platform tables and migration 006. */
SET XACT_ABORT ON;
SET NOCOUNT ON;
GO
IF OBJECT_ID(N'dbo.UnverifiedAccountCleanupLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UnverifiedAccountCleanupLog (
        RunId uniqueidentifier NOT NULL,
        ClientId bigint NOT NULL,
        AccountCreatedUtc datetime2(3) NOT NULL,
        DeletedUtc datetime2(3) NOT NULL,
        CONSTRAINT PK_UnverifiedAccountCleanupLog PRIMARY KEY (RunId, ClientId)
    );
    -- Deliberately no FK to the deleted customer and no email/password/token copy.
    CREATE INDEX IX_UnverifiedAccountCleanupLog_DeletedUtc
        ON dbo.UnverifiedAccountCleanupLog(DeletedUtc);
END;
GO
IF OBJECT_ID(N'dbo.CleanupUnverifiedAccounts', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.CleanupUnverifiedAccounts AS RETURN;');
GO
ALTER PROCEDURE dbo.CleanupUnverifiedAccounts
    @DryRun bit = 1,
    @GraceHours int = 48,
    @BatchSize int = 500
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @DryRun IS NULL OR @GraceHours IS NULL OR @GraceHours < 1 OR @GraceHours > 8760
       OR @BatchSize IS NULL OR @BatchSize < 1 OR @BatchSize > 5000
        THROW 50010, 'Use DryRun 0/1, GraceHours 1..8760, BatchSize 1..5000.', 1;
    -- This procedure owns its transaction and must not commit/rollback a caller's work.
    IF @@TRANCOUNT <> 0
        THROW 50011, 'Run cleanup outside an existing transaction.', 1;

    DECLARE @Now datetime2(3) = SYSUTCDATETIME();
    DECLARE @Cutoff datetime2(3) = DATEADD(hour, -@GraceHours, @Now);
    DECLARE @RunId uniqueidentifier = NEWID();
    DECLARE @LockResult int;
    DECLARE @Targets TABLE (ClientId bigint NOT NULL PRIMARY KEY);
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC @LockResult = sys.sp_getapplock
            @Resource = N'AutoDealer.CleanupUnverifiedAccounts',
            @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0;
        IF @LockResult < 0
            THROW 50012, 'Another unverified-account cleanup is running.', 1;

        -- Hold customer locks until commit so activation cannot race deletion.
        INSERT @Targets (ClientId)
        SELECT TOP (@BatchSize) c.ClientId
        FROM dbo.Clients c WITH (XLOCK, HOLDLOCK)
        WHERE c.Status = 'pending'
          AND c.EmailVerifiedUtc IS NULL
          AND c.CreatedUtc <= @Cutoff
          AND NOT EXISTS (
              SELECT 1 FROM dbo.ClientEmailVerifications v WITH (HOLDLOCK)
              WHERE v.ClientId = c.ClientId
                AND (v.UsedUtc IS NOT NULL OR v.ExpiresUtc > @Now))
          -- Never automatically purge accounts with payment/billing evidence.
          AND NOT EXISTS (
              SELECT 1 FROM dbo.PaymentProfiles p WITH (HOLDLOCK) WHERE p.ClientId = c.ClientId)
          AND NOT EXISTS (
              SELECT 1 FROM dbo.Subscriptions s WITH (HOLDLOCK)
              WHERE s.ClientId = c.ClientId
                AND (s.Status = 'active' OR NULLIF(LTRIM(RTRIM(s.ProviderSubscriptionId)), '') IS NOT NULL))
        ORDER BY c.CreatedUtc, c.ClientId;

        IF @DryRun = 1
        BEGIN
            SELECT c.ClientId, c.ClientNumber, c.Email, c.CreatedUtc, c.Status,
                   @GraceHours AS GraceHours
            FROM dbo.Clients c JOIN @Targets t ON t.ClientId = c.ClientId
            ORDER BY c.CreatedUtc, c.ClientId;
            COMMIT TRANSACTION;
            RETURN;
        END;

        -- Dependency order follows the existing administrative deletion workflow.
        DELETE l FROM dbo.ApiUsageLog l JOIN @Targets t ON t.ClientId=l.ClientId;
        DELETE d FROM dbo.ApiUsageDaily d JOIN @Targets t ON t.ClientId=d.ClientId;
        DELETE k FROM dbo.ApiKeys k JOIN @Targets t ON t.ClientId=k.ClientId;
        DELETE h FROM dbo.ClientEmailHistory h JOIN @Targets t ON t.ClientId=h.ClientId;
        DELETE v FROM dbo.ClientEmailVerifications v JOIN @Targets t ON t.ClientId=v.ClientId;
        DELETE p FROM dbo.PaymentProfiles p JOIN @Targets t ON t.ClientId=p.ClientId;
        DELETE cr FROM dbo.ClientCredentials cr JOIN @Targets t ON t.ClientId=cr.ClientId;
        DELETE s FROM dbo.Subscriptions s JOIN @Targets t ON t.ClientId=s.ClientId;
        DELETE c
        OUTPUT @RunId, deleted.ClientId, deleted.CreatedUtc, @Now
            INTO dbo.UnverifiedAccountCleanupLog (RunId,ClientId,AccountCreatedUtc,DeletedUtc)
        FROM dbo.Clients c JOIN @Targets t ON t.ClientId=c.ClientId;
        DECLARE @Deleted int = @@ROWCOUNT;
        COMMIT TRANSACTION;
        SELECT @RunId AS RunId, @Deleted AS DeletedAccounts, @Now AS CompletedUtc;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO
/* Grant only EXECUTE to a dedicated maintenance database user if needed:
   GRANT EXECUTE ON dbo.CleanupUnverifiedAccounts TO YourMaintenanceUser;
   Same-owner static SQL uses ownership chaining. Do not grant this to the web user.
   If an unexpected FK exists, the entire batch rolls back; do not disable FKs. */
