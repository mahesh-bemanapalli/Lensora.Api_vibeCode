USE Lensora;
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.Photographers', N'StorageLimitBytes') IS NULL
  ALTER TABLE dbo.Photographers ADD StorageLimitBytes BIGINT NOT NULL
    CONSTRAINT DF_Photographers_StorageLimitBytes DEFAULT (5000000000) WITH VALUES;
IF OBJECT_ID(N'dbo.MediaAssets', N'U') IS NULL
BEGIN
  CREATE TABLE dbo.MediaAssets (
    Id BIGINT IDENTITY PRIMARY KEY,
    PhotographerId INT NOT NULL,
    FileName NVARCHAR(255) NOT NULL,
    Url NVARCHAR(2048) NOT NULL,
    PublicId NVARCHAR(255) NULL,
    ContentHash NVARCHAR(64) NULL,
    Kind NVARCHAR(16) NOT NULL,
    ContentType NVARCHAR(100) NOT NULL,
    SizeBytes BIGINT NULL,
    Status NVARCHAR(16) NOT NULL,
    CreatedUtc DATETIME2 NOT NULL,
    CONSTRAINT FK_MediaAssets_Photographers FOREIGN KEY (PhotographerId) REFERENCES dbo.Photographers(Id),
    CONSTRAINT CK_MediaAssets_Size CHECK (SizeBytes IS NULL OR SizeBytes >= 0)
  );
  CREATE INDEX IX_MediaAssets_PhotographerId_ContentHash ON dbo.MediaAssets(PhotographerId,ContentHash);
  CREATE INDEX IX_MediaAssets_PhotographerId ON dbo.MediaAssets(PhotographerId);
END;
COMMIT TRANSACTION;
GO
/* Import referenced images once per photographer and Cloudinary public ID or URL.
   External links are visible but do not consume managed storage. Cloudinary sizes
   stay unknown until the owner runs Sync existing media. */
WITH ReferencesToImport AS (
  SELECT Id PhotographerId, ProfileImageUrl Url, ProfileImagePublicId PublicId FROM dbo.Photographers
  UNION ALL SELECT PhotographerId, ImageUrl, ImagePublicId FROM dbo.PortfolioItems
  UNION ALL SELECT PhotographerId, ImageUrl, ImagePublicId FROM dbo.GearItems
  UNION ALL SELECT PhotographerId, ImageUrl, ImagePublicId FROM dbo.Packages
), UniqueReferences AS (
  SELECT *, ROW_NUMBER() OVER(PARTITION BY PhotographerId, COALESCE(NULLIF(PublicId,''),Url) ORDER BY Url) rn
  FROM ReferencesToImport WHERE NULLIF(Url,'') IS NOT NULL
)
INSERT dbo.MediaAssets(PhotographerId,FileName,Url,PublicId,ContentHash,Kind,ContentType,SizeBytes,Status,CreatedUtc)
SELECT r.PhotographerId,N'Existing image',r.Url,NULLIF(r.PublicId,''),NULL,N'image',N'image/unknown',NULL,
  CASE WHEN NULLIF(r.PublicId,'') IS NULL THEN N'External' ELSE N'Ready' END,SYSUTCDATETIME()
FROM UniqueReferences r WHERE rn=1 AND NOT EXISTS (
 SELECT 1 FROM dbo.MediaAssets a WHERE a.PhotographerId=r.PhotographerId
 AND ((NULLIF(r.PublicId,'') IS NOT NULL AND a.PublicId=r.PublicId) OR a.Url=r.Url));
GO
