USE Lensora;
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.TemplateAssignments', N'U') IS NULL
BEGIN
  CREATE TABLE dbo.TemplateAssignments (
    Id BIGINT IDENTITY PRIMARY KEY,
    PhotographerId INT NOT NULL,
    AssignedByUserId INT NOT NULL,
    PreviousTemplateKey NVARCHAR(40) NOT NULL,
    TemplateKey NVARCHAR(40) NOT NULL,
    AssignedUtc DATETIME2 NOT NULL,
    CONSTRAINT FK_TemplateAssignments_Photographers FOREIGN KEY (PhotographerId) REFERENCES dbo.Photographers(Id),
    CONSTRAINT FK_TemplateAssignments_Users FOREIGN KEY (AssignedByUserId) REFERENCES dbo.Users(Id),
    CONSTRAINT CK_TemplateAssignments_Previous CHECK (PreviousTemplateKey IN (N'editorial', N'minimal-gallery')),
    CONSTRAINT CK_TemplateAssignments_Template CHECK (TemplateKey IN (N'editorial', N'minimal-gallery'))
  );
  CREATE INDEX IX_TemplateAssignments_PhotographerId_AssignedUtc ON dbo.TemplateAssignments(PhotographerId, AssignedUtc);
END;
COMMIT TRANSACTION;
GO
