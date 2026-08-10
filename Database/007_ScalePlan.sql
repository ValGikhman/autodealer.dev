/* Adds the Scale subscription tier without changing existing plan assignments. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

MERGE dbo.Plans WITH (HOLDLOCK) AS target
USING (VALUES
    ('SCALE', N'SCALE', CAST(300.00 AS decimal(10,2)), 500, 30)
) AS source (PlanCode, DisplayName, MonthlyPrice, MonthlyRequestQuota, MaxApiKeys)
ON target.PlanCode = source.PlanCode
WHEN MATCHED THEN UPDATE SET
    DisplayName = source.DisplayName,
    MonthlyPrice = source.MonthlyPrice,
    MonthlyRequestQuota = source.MonthlyRequestQuota,
    MaxApiKeys = source.MaxApiKeys,
    IsActive = 1
WHEN NOT MATCHED THEN INSERT
    (PlanCode, DisplayName, MonthlyPrice, MonthlyRequestQuota, MaxApiKeys, IsActive)
    VALUES
    (source.PlanCode, source.DisplayName, source.MonthlyPrice, source.MonthlyRequestQuota, source.MaxApiKeys, 1);
GO
