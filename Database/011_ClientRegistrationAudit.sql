/* Run in the APPLICATION database before publishing the application.
   No customer data is deleted. Audit rows deliberately have no customer FK. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
IF OBJECT_ID('dbo.ClientRegistrationAudit','U') IS NULL
BEGIN
 CREATE TABLE dbo.ClientRegistrationAudit (
  AuditId uniqueidentifier NOT NULL PRIMARY KEY,
  RecordedUtc datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
  CompletedUtc datetime2(3) NULL, ClientId bigint NULL,
  EventType varchar(12) NOT NULL DEFAULT 'submission',
  Source varchar(12) NOT NULL, Outcome varchar(16) NOT NULL,
  SourceIp varchar(45) NULL, PeerIp varchar(45) NULL,
  ForwardedFor nvarchar(1024) NULL, TrustedProxy bit NOT NULL DEFAULT 0,
  UserAgent nvarchar(512) NULL, AcceptLanguage nvarchar(256) NULL,
  VisitorHash binary(32) NULL, CountryCode char(2) NULL,
  RequestPath nvarchar(300) NULL, PlanCode nvarchar(32) NULL,
  Actor nvarchar(254) NULL
 );
 CREATE INDEX IX_ClientRegistrationAudit_Client ON dbo.ClientRegistrationAudit(ClientId,RecordedUtc DESC);
 CREATE INDEX IX_ClientRegistrationAudit_Ip ON dbo.ClientRegistrationAudit(SourceIp,RecordedUtc DESC) INCLUDE(EventType,Outcome,Source);
 CREATE INDEX IX_ClientRegistrationAudit_Visitor ON dbo.ClientRegistrationAudit(VisitorHash,RecordedUtc DESC);
 CREATE INDEX IX_ClientRegistrationAudit_Date ON dbo.ClientRegistrationAudit(RecordedUtc);
END;
IF OBJECT_ID('dbo.ClientRegistrationIpBlocks','U') IS NULL
 CREATE TABLE dbo.ClientRegistrationIpBlocks (
  SourceIp varchar(45) NOT NULL PRIMARY KEY,
  BlockedUntilUtc datetime2(3) NOT NULL,
  UpdatedUtc datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
  UpdatedBy nvarchar(254) NOT NULL
 );
GO
/* Change this to the WEBSITE'S login, which can differ from the cleanup script. */
DECLARE @ApplicationLogin sysname=N'AUTODEALER';
DECLARE @DbUser sysname=(SELECT TOP(1) name FROM sys.database_principals
 WHERE type IN('S','U','G') AND (sid=SUSER_SID(@ApplicationLogin) OR name=@ApplicationLogin)
 ORDER BY CASE WHEN sid=SUSER_SID(@ApplicationLogin) THEN 0 ELSE 1 END);
IF @DbUser IS NULL THROW 50045,'Set ApplicationLogin to the website login or its existing database user name and rerun.',1;
IF SUSER_SID(@ApplicationLogin) IS NULL
 PRINT 'No matching server login was found. Permissions are being granted to the existing database user by name. Windows-authenticated dbo already has access; SQL authentication requires a correctly mapped server login.';
DECLARE @Sql nvarchar(max)=
 N'GRANT SELECT, INSERT, UPDATE ON dbo.ClientRegistrationAudit TO '+QUOTENAME(@DbUser)+N';'+
 N'GRANT SELECT, INSERT, UPDATE ON dbo.ClientRegistrationIpBlocks TO '+QUOTENAME(@DbUser)+N';';
EXEC sys.sp_executesql @Sql;
GO
