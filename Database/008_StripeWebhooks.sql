/* Stripe webhook idempotency and subscription reconciliation. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.StripeWebhookEvents', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.StripeWebhookEvents (
        StripeEventId nvarchar(255) NOT NULL CONSTRAINT PK_StripeWebhookEvents PRIMARY KEY,
        EventType nvarchar(100) NOT NULL,
        EventCreatedUtc datetime2(3) NOT NULL,
        ProcessedUtc datetime2(3) NOT NULL CONSTRAINT DF_StripeWebhookEvents_ProcessedUtc DEFAULT (SYSUTCDATETIME())
    );
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.Subscriptions')
      AND name = 'UX_Subscriptions_ProviderSubscriptionId'
)
BEGIN
    CREATE UNIQUE INDEX UX_Subscriptions_ProviderSubscriptionId
        ON dbo.Subscriptions(ProviderSubscriptionId)
        WHERE ProviderSubscriptionId IS NOT NULL;
END;
GO

/* Optional least-privilege grants; replace AutoDealerWeb with the deployed web principal.
GRANT SELECT, UPDATE ON dbo.Subscriptions TO AutoDealerWeb;
GRANT SELECT ON dbo.Clients TO AutoDealerWeb;
GRANT SELECT, INSERT ON dbo.StripeWebhookEvents TO AutoDealerWeb;
*/
