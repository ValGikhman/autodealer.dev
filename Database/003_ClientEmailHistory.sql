/* Store the complete rendered HTML for email sent in relation to a client. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.ClientEmailHistory', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ClientEmailHistory (
        ClientEmailHistoryId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ClientEmailHistory PRIMARY KEY,
        ClientId bigint NOT NULL,
        SentUtc datetime2(3) NOT NULL CONSTRAINT DF_ClientEmailHistory_SentUtc DEFAULT (SYSUTCDATETIME()),
        ToEmail nvarchar(254) NOT NULL,
        Subject nvarchar(998) NOT NULL,
        HtmlBody nvarchar(max) NOT NULL,
        CONSTRAINT FK_ClientEmailHistory_Client FOREIGN KEY (ClientId) REFERENCES dbo.Clients(ClientId) ON DELETE CASCADE
    );

    CREATE INDEX IX_ClientEmailHistory_Client_SentUtc
        ON dbo.ClientEmailHistory(ClientId, SentUtc DESC)
        INCLUDE (ToEmail, Subject);
END;
GO

/* Preserve history by preventing a client row from being deleted while email records exist. */
IF EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE parent_object_id = OBJECT_ID('dbo.ClientEmailHistory')
      AND name = 'FK_ClientEmailHistory_Client'
      AND delete_referential_action <> 0
)
BEGIN
    ALTER TABLE dbo.ClientEmailHistory DROP CONSTRAINT FK_ClientEmailHistory_Client;
    ALTER TABLE dbo.ClientEmailHistory WITH CHECK ADD CONSTRAINT FK_ClientEmailHistory_Client
        FOREIGN KEY (ClientId) REFERENCES dbo.Clients(ClientId) ON DELETE CASCADE;
END;
GO

/* Optional least-privilege grant; replace AutoDealerWeb with your database user.
GRANT SELECT, INSERT ON dbo.ClientEmailHistory TO AutoDealerWeb;
*/
