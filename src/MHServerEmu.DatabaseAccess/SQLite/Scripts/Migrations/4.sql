-- Migration from schema version 4 to version 5
-- Adds HWID ban support: BannedHardware table and LastKnownMachineId column

-- Create BannedHardware table
CREATE TABLE IF NOT EXISTS "BannedHardware" (
	"Hwid"	TEXT PRIMARY KEY,
	"BannedBy"	TEXT,
	"Reason"	TEXT,
	"BanDate"	TEXT
);

-- Add LastKnownMachineId column to Account table
ALTER TABLE "Account" ADD COLUMN "LastKnownMachineId" TEXT;

-- Update schema version
PRAGMA user_version=5;