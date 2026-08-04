/* Persist dealer-demo leads before notification delivery. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.DealerDemoRequests', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DealerDemoRequests (
        RequestId uniqueidentifier NOT NULL CONSTRAINT PK_DealerDemoRequests PRIMARY KEY,
        BusinessName nvarchar(160) NOT NULL,
        ContactName nvarchar(160) NOT NULL,
        Email nvarchar(254) NOT NULL,
        Phone nvarchar(32) NULL,
        CurrentWebsite nvarchar(300) NULL,
        LocationCount int NULL,
        InventorySize nvarchar(80) NOT NULL,
        PrimaryGoal nvarchar(120) NOT NULL,
        PreferredContact varchar(20) NOT NULL,
        Message nvarchar(3000) NOT NULL,
        Status varchar(20) NOT NULL CONSTRAINT DF_DealerDemoRequests_Status DEFAULT ('new'),
        CreatedUtc datetime2(3) NOT NULL CONSTRAINT DF_DealerDemoRequests_CreatedUtc DEFAULT (SYSUTCDATETIME()),
        OwnerNotificationSentUtc datetime2(3) NULL,
        CustomerConfirmationSentUtc datetime2(3) NULL,
        CONSTRAINT CK_DealerDemoRequests_Status CHECK (Status IN ('new','contacted','qualified','closed')),
        CONSTRAINT CK_DealerDemoRequests_PreferredContact CHECK (PreferredContact IN ('Email','Phone')),
        CONSTRAINT CK_DealerDemoRequests_LocationCount CHECK (LocationCount IS NULL OR LocationCount BETWEEN 1 AND 1000),
        CONSTRAINT CK_DealerDemoRequests_PhonePreference CHECK (PreferredContact <> 'Phone' OR Phone IS NOT NULL)
    );

    CREATE INDEX IX_DealerDemoRequests_Status_Created
        ON dbo.DealerDemoRequests(Status, CreatedUtc DESC);
    CREATE INDEX IX_DealerDemoRequests_Email_Created
        ON dbo.DealerDemoRequests(Email, CreatedUtc DESC);
END;
GO

/* Optional least-privilege grants; replace AutoDealerWeb with your database user.
GRANT SELECT, INSERT, UPDATE ON dbo.DealerDemoRequests TO AutoDealerWeb;
*/
