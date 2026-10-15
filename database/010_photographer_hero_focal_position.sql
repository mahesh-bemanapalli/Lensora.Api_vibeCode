/* Add per-photographer focal positioning for the hero cover image. */
USE Lensora;
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.Photographers', N'HeroFocalX') IS NULL
  ALTER TABLE dbo.Photographers ADD HeroFocalX INT NOT NULL
    CONSTRAINT DF_Photographers_HeroFocalX DEFAULT (50) WITH VALUES;

IF COL_LENGTH(N'dbo.Photographers', N'HeroFocalY') IS NULL
  ALTER TABLE dbo.Photographers ADD HeroFocalY INT NOT NULL
    CONSTRAINT DF_Photographers_HeroFocalY DEFAULT (50) WITH VALUES;

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
  WHERE parent_object_id = OBJECT_ID(N'dbo.Photographers')
    AND name = N'CK_Photographers_HeroFocalX')
  EXEC(N'ALTER TABLE dbo.Photographers WITH CHECK ADD CONSTRAINT CK_Photographers_HeroFocalX
    CHECK (HeroFocalX BETWEEN 0 AND 100)');

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
  WHERE parent_object_id = OBJECT_ID(N'dbo.Photographers')
    AND name = N'CK_Photographers_HeroFocalY')
  EXEC(N'ALTER TABLE dbo.Photographers WITH CHECK ADD CONSTRAINT CK_Photographers_HeroFocalY
    CHECK (HeroFocalY BETWEEN 0 AND 100)');

COMMIT TRANSACTION;
GO
