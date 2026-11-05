-- Run before deploying the admin full-credential feature.
IF COL_LENGTH('dbo.ApiKeys', 'ProtectedCredential') IS NULL
    ALTER TABLE dbo.ApiKeys ADD ProtectedCredential nvarchar(max) NULL;
