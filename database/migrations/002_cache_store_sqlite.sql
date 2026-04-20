CREATE TABLE IF NOT EXISTS CachedDecisions
(
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CacheKey TEXT NOT NULL,
    Allowed INTEGER NOT NULL,
    RiskScore REAL NOT NULL,
    Reason TEXT NOT NULL,
    ReasonDetailsJson TEXT NOT NULL DEFAULT '[]',
    IsAnomaly INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL,
    ExpiresAt TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS IX_CachedDecisions_CacheKey
    ON CachedDecisions (CacheKey);

CREATE INDEX IF NOT EXISTS IX_CachedDecisions_ExpiresAt
    ON CachedDecisions (ExpiresAt);

CREATE TABLE IF NOT EXISTS SecurityEvents
(
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    UserId TEXT NOT NULL,
    SourceIp TEXT NOT NULL,
    Path TEXT NOT NULL,
    Allowed INTEGER NOT NULL,
    RiskScore REAL NOT NULL,
    Message TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    CreatedAtUnixMs INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS IX_SecurityEvents_CreatedAtUnixMs
    ON SecurityEvents (CreatedAtUnixMs);

CREATE TABLE IF NOT EXISTS BlockedIpEntries
(
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    SourceIp TEXT NOT NULL,
    Reason TEXT NOT NULL,
    BlockedAt TEXT NOT NULL,
    ExpiresAt TEXT NULL
);

CREATE INDEX IF NOT EXISTS IX_BlockedIpEntries_SourceIp
    ON BlockedIpEntries (SourceIp);
