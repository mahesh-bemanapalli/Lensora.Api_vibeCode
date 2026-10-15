/* Run once after 003_add_packages.sql. Duplicate orders are renumbered
   from 0 within affected photographers, preserving order by old value and ID. */
USE Lensora;
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;

;WITH Ranked AS (
  SELECT Id, PhotographerId, DisplayOrder,
         ROW_NUMBER() OVER (PARTITION BY PhotographerId ORDER BY DisplayOrder, Id) - 1 AS NewOrder
  FROM dbo.PortfolioItems
), DuplicateOwners AS (
  SELECT PhotographerId FROM dbo.PortfolioItems
  GROUP BY PhotographerId HAVING COUNT(*) <> COUNT(DISTINCT DisplayOrder)
)
UPDATE p SET DisplayOrder = r.NewOrder
FROM dbo.PortfolioItems p
JOIN Ranked r ON r.Id = p.Id
JOIN DuplicateOwners d ON d.PhotographerId = p.PhotographerId;

;WITH Ranked AS (
  SELECT Id, PhotographerId, DisplayOrder,
         ROW_NUMBER() OVER (PARTITION BY PhotographerId ORDER BY DisplayOrder, Id) - 1 AS NewOrder
  FROM dbo.GearItems
), DuplicateOwners AS (
  SELECT PhotographerId FROM dbo.GearItems
  GROUP BY PhotographerId HAVING COUNT(*) <> COUNT(DISTINCT DisplayOrder)
)
UPDATE g SET DisplayOrder = r.NewOrder
FROM dbo.GearItems g
JOIN Ranked r ON r.Id = g.Id
JOIN DuplicateOwners d ON d.PhotographerId = g.PhotographerId;

;WITH Ranked AS (
  SELECT Id, PhotographerId, DisplayOrder,
         ROW_NUMBER() OVER (PARTITION BY PhotographerId ORDER BY DisplayOrder, Id) - 1 AS NewOrder
  FROM dbo.Packages
), DuplicateOwners AS (
  SELECT PhotographerId FROM dbo.Packages
  GROUP BY PhotographerId HAVING COUNT(*) <> COUNT(DISTINCT DisplayOrder)
)
UPDATE p SET DisplayOrder = r.NewOrder
FROM dbo.Packages p
JOIN Ranked r ON r.Id = p.Id
JOIN DuplicateOwners d ON d.PhotographerId = p.PhotographerId;

DROP INDEX IX_PortfolioItems_PhotographerId_DisplayOrder ON dbo.PortfolioItems;
DROP INDEX IX_GearItems_PhotographerId_DisplayOrder ON dbo.GearItems;
CREATE UNIQUE INDEX UX_PortfolioItems_PhotographerId_DisplayOrder ON dbo.PortfolioItems (PhotographerId, DisplayOrder);
CREATE UNIQUE INDEX UX_GearItems_PhotographerId_DisplayOrder ON dbo.GearItems (PhotographerId, DisplayOrder);
CREATE UNIQUE INDEX UX_Packages_PhotographerId_DisplayOrder ON dbo.Packages (PhotographerId, DisplayOrder);

COMMIT TRANSACTION;
GO
