/* Lensora SQL Server schema. Execute once in the target database. */
IF DB_ID(N'Lensora') IS NULL CREATE DATABASE Lensora;
GO
USE Lensora;
GO
CREATE TABLE dbo.Users (
  Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
  Email NVARCHAR(320) NOT NULL,
  PasswordHash NVARCHAR(255) NOT NULL,
  Role NVARCHAR(32) NOT NULL CONSTRAINT DF_Users_Role DEFAULT N'Photographer',
  CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_Users_CreatedUtc DEFAULT SYSUTCDATETIME(),
  CONSTRAINT UQ_Users_Email UNIQUE (Email)
);
CREATE TABLE dbo.Photographers (
  Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Photographers PRIMARY KEY,
  UserId INT NOT NULL,
  Name NVARCHAR(100) NOT NULL,
  Slug NVARCHAR(120) NOT NULL,
  Bio NVARCHAR(2000) NULL,
  Location NVARCHAR(160) NULL,
  ProfileImageUrl NVARCHAR(2048) NULL,
  ProfileImagePublicId NVARCHAR(255) NULL,
  HeroFocalX INT NOT NULL CONSTRAINT DF_Photographers_HeroFocalX DEFAULT 50,
  HeroFocalY INT NOT NULL CONSTRAINT DF_Photographers_HeroFocalY DEFAULT 50,
  CONSTRAINT UQ_Photographers_UserId UNIQUE (UserId),
  CONSTRAINT UQ_Photographers_Slug UNIQUE (Slug),
  CONSTRAINT CK_Photographers_HeroFocalX CHECK (HeroFocalX BETWEEN 0 AND 100),
  CONSTRAINT CK_Photographers_HeroFocalY CHECK (HeroFocalY BETWEEN 0 AND 100),
  CONSTRAINT FK_Photographers_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(Id) ON DELETE CASCADE
);
CREATE TABLE dbo.PortfolioItems (
  Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PortfolioItems PRIMARY KEY,
  PhotographerId INT NOT NULL,
  ImageUrl NVARCHAR(2048) NOT NULL,
  ImagePublicId NVARCHAR(255) NULL,
  Title NVARCHAR(200) NULL,
  Category NVARCHAR(100) NULL,
  DisplayOrder INT NOT NULL CONSTRAINT DF_PortfolioItems_DisplayOrder DEFAULT 0,
  CONSTRAINT FK_PortfolioItems_Photographers FOREIGN KEY (PhotographerId) REFERENCES dbo.Photographers(Id) ON DELETE CASCADE
);
CREATE TABLE dbo.GearItems (
  Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_GearItems PRIMARY KEY,
  PhotographerId INT NOT NULL,
  Name NVARCHAR(160) NOT NULL,
  Category NVARCHAR(60) NOT NULL,
  Brand NVARCHAR(100) NOT NULL,
  Model NVARCHAR(160) NOT NULL,
  Description NVARCHAR(1000) NULL,
  ImageUrl NVARCHAR(2048) NULL,
  ImagePublicId NVARCHAR(255) NULL,
  IsFeatured BIT NOT NULL CONSTRAINT DF_GearItems_IsFeatured DEFAULT 0,
  DisplayOrder INT NOT NULL CONSTRAINT DF_GearItems_DisplayOrder DEFAULT 0,
  CONSTRAINT FK_GearItems_Photographers FOREIGN KEY (PhotographerId) REFERENCES dbo.Photographers(Id) ON DELETE CASCADE
);
CREATE TABLE dbo.Bookings (
  Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Bookings PRIMARY KEY,
  PhotographerId INT NOT NULL,
  ClientName NVARCHAR(120) NOT NULL,
  ClientEmail NVARCHAR(320) NOT NULL,
  EventDate DATE NOT NULL,
  Message NVARCHAR(2000) NULL,
  Status NVARCHAR(20) NOT NULL CONSTRAINT DF_Bookings_Status DEFAULT N'Pending',
  CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_Bookings_CreatedUtc DEFAULT SYSUTCDATETIME(),
  CONSTRAINT CK_Bookings_Status CHECK (Status IN (N'Pending', N'Accepted', N'Rejected')),
  CONSTRAINT FK_Bookings_Photographers FOREIGN KEY (PhotographerId) REFERENCES dbo.Photographers(Id) ON DELETE CASCADE
);
CREATE INDEX IX_Bookings_PhotographerId_CreatedUtc ON dbo.Bookings (PhotographerId, CreatedUtc DESC);
CREATE INDEX IX_PortfolioItems_PhotographerId_DisplayOrder ON dbo.PortfolioItems (PhotographerId, DisplayOrder);
CREATE INDEX IX_GearItems_PhotographerId_DisplayOrder ON dbo.GearItems (PhotographerId, DisplayOrder);
GO
