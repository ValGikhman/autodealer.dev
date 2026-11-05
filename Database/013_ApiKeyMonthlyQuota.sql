-- Optional per-key monthly request quota, managed only by Majordome admins.
-- Existing keys keep their plan limit. No table or stored procedure is created.
IF COL_LENGTH('dbo.ApiKeys','IsQuotaUnlimited') IS NULL
    ALTER TABLE dbo.ApiKeys ADD IsQuotaUnlimited bit NOT NULL
        CONSTRAINT DF_ApiKeys_IsQuotaUnlimited DEFAULT(0);
