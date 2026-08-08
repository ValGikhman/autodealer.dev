/* Idempotent state used by the scheduled trial-expiration job. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.Subscriptions', 'TrialExpirationNoticeAttemptedUtc') IS NULL
    ALTER TABLE dbo.Subscriptions ADD TrialExpirationNoticeAttemptedUtc datetime2(3) NULL;
GO

IF COL_LENGTH('dbo.Subscriptions', 'TrialExpiredUtc') IS NULL
    ALTER TABLE dbo.Subscriptions ADD TrialExpiredUtc datetime2(3) NULL;
GO

IF COL_LENGTH('dbo.Subscriptions', 'TrialExpirationNoticeSentUtc') IS NULL
    ALTER TABLE dbo.Subscriptions ADD TrialExpirationNoticeSentUtc datetime2(3) NULL;
GO

IF COL_LENGTH('dbo.Subscriptions', 'TrialExpirationNoticeAttemptCount') IS NULL
    ALTER TABLE dbo.Subscriptions ADD TrialExpirationNoticeAttemptCount int NOT NULL
        CONSTRAINT DF_Subscriptions_TrialNoticeAttempts DEFAULT (0);
GO

IF COL_LENGTH('dbo.Subscriptions', 'TrialExpirationNoticeError') IS NULL
    ALTER TABLE dbo.Subscriptions ADD TrialExpirationNoticeError nvarchar(1000) NULL;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.Subscriptions')
      AND name = 'IX_Subscriptions_TrialExpirationNotice'
)
BEGIN
    CREATE INDEX IX_Subscriptions_TrialExpirationNotice
        ON dbo.Subscriptions(Status, CurrentPeriodEndUtc, TrialExpirationNoticeSentUtc)
        INCLUDE (ClientId, TrialExpiredUtc, TrialExpirationNoticeAttemptedUtc, TrialExpirationNoticeAttemptCount);
END;
GO

/* Optional least-privilege grants; replace AutoDealerWeb with the scheduled-job principal.
GRANT SELECT, UPDATE ON dbo.Subscriptions TO AutoDealerWeb;
GRANT SELECT ON dbo.Clients TO AutoDealerWeb;
GRANT INSERT ON dbo.ClientEmailHistory TO AutoDealerWeb;
*/
