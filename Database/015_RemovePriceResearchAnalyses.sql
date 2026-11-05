SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.PriceResearchBidScenarios', N'U') IS NOT NULL
    DROP TABLE dbo.PriceResearchBidScenarios;

IF OBJECT_ID(N'dbo.PriceResearchAnalyses', N'U') IS NOT NULL
    DROP TABLE dbo.PriceResearchAnalyses;

COMMIT TRANSACTION;
