/* Run once against an existing Lensora database. */
USE Lensora;
GO
CREATE TABLE dbo.Packages (
  Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Packages PRIMARY KEY,
  PhotographerId INT NOT NULL,
  Name NVARCHAR(160) NOT NULL,
  Description NVARCHAR(2000) NOT NULL,
  ImageUrl NVARCHAR(2048) NOT NULL,
  ImagePublicId NVARCHAR(255) NULL,
  Price DECIMAL(12,2) NOT NULL,
  Currency NVARCHAR(3) NOT NULL,
  CoverageHours INT NULL,
  Deliverables NVARCHAR(2000) NULL,
  IsPublished BIT NOT NULL CONSTRAINT DF_Packages_IsPublished DEFAULT 0,
  DisplayOrder INT NOT NULL CONSTRAINT DF_Packages_DisplayOrder DEFAULT 0,
  CONSTRAINT FK_Packages_Photographers FOREIGN KEY (PhotographerId) REFERENCES dbo.Photographers(Id) ON DELETE CASCADE,
  CONSTRAINT CK_Packages_Price CHECK (Price >= 0),
  CONSTRAINT CK_Packages_CoverageHours CHECK (CoverageHours IS NULL OR CoverageHours > 0)
);
CREATE INDEX IX_Packages_PhotographerId_IsPublished_DisplayOrder ON dbo.Packages (PhotographerId, IsPublished, DisplayOrder);
ALTER TABLE dbo.Bookings ADD PackageId INT NULL;
ALTER TABLE dbo.Bookings ADD CONSTRAINT FK_Bookings_Packages FOREIGN KEY (PackageId) REFERENCES dbo.Packages(Id);
CREATE INDEX IX_Bookings_PackageId ON dbo.Bookings (PackageId);
GO
