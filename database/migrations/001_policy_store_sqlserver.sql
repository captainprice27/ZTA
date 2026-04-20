IF DB_ID('ZtaPolicy') IS NULL
BEGIN
    CREATE DATABASE ZtaPolicy;
END
GO

USE ZtaPolicy;
GO

IF OBJECT_ID('dbo.AccessPolicies', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AccessPolicies
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        UserId NVARCHAR(120) NOT NULL,
        PathPrefix NVARCHAR(200) NOT NULL,
        HttpMethod NVARCHAR(10) NOT NULL,
        IsEnabled BIT NOT NULL DEFAULT(1)
    );

    CREATE INDEX IX_AccessPolicies_UserId_PathPrefix_HttpMethod
        ON dbo.AccessPolicies (UserId, PathPrefix, HttpMethod);
END
GO
