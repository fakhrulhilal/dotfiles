-- App user and database; idempotent, safe to re-run
IF DB_ID(N'app') IS NULL
    CREATE DATABASE [app];
GO

IF SUSER_ID(N'app') IS NULL
    CREATE LOGIN [app] WITH PASSWORD = N'app', DEFAULT_DATABASE = [app], CHECK_POLICY = OFF;
GO

ALTER AUTHORIZATION ON DATABASE::[app] TO [app];
GO

/*
-- Grant user to existing DB
USE [app];
IF USER_ID(N'app') IS NULL
    CREATE USER [app] FOR LOGIN [app];
ALTER ROLE db_owner ADD MEMBER [app];
*/
