/* Run this only when 001_create_lensora.sql was executed before Cloudinary support was added. */
USE Lensora;
GO
IF COL_LENGTH(N'dbo.Photographers', N'ProfileImagePublicId') IS NULL
    ALTER TABLE dbo.Photographers ADD ProfileImagePublicId NVARCHAR(255) NULL;
IF COL_LENGTH(N'dbo.PortfolioItems', N'ImagePublicId') IS NULL
    ALTER TABLE dbo.PortfolioItems ADD ImagePublicId NVARCHAR(255) NULL;
IF COL_LENGTH(N'dbo.GearItems', N'ImagePublicId') IS NULL
    ALTER TABLE dbo.GearItems ADD ImagePublicId NVARCHAR(255) NULL;
GO
