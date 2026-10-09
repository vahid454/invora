-- Run once as the migration/database owner. Supply runtime_password through psql -v.
-- The runtime role cannot own tables, migrate, or disable history triggers.
CREATE ROLE invora_runtime LOGIN PASSWORD :'runtime_password';
GRANT CONNECT ON DATABASE :"database_name" TO invora_runtime;
GRANT USAGE ON SCHEMA public TO invora_runtime;
GRANT SELECT, INSERT ON ALL TABLES IN SCHEMA public TO invora_runtime;
GRANT UPDATE ON "Businesses", "Branches", "StaffUsers", "AuthSessions", "RefreshCredentials", "BusinessSettings", "ProductModel", "ProductVariant", "TaxRate", "Party", "StockUnit", "StockBalance", "StockCostLayer", "SaleCostAllocation", "StockTransfer", "StockTransferItem", "DocumentSequence", "Sale", "Purchase", "ImportJob" TO invora_runtime;
GRANT DELETE ON "StaffBranches" TO invora_runtime;
-- Reapply grants for new tables after reviewing each future migration.
REVOKE INSERT ON "__EFMigrationsHistory" FROM invora_runtime;
