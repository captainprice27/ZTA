# Database Migration Scripts

These SQL scripts exist because the runtime now prefers `MigrateAsync()` but the repository cannot generate EF migration classes in this sandbox:

- `dotnet-ef` is not installed
- outbound NuGet/tool download is blocked here

Use these scripts as the checked-in baseline for local SQL Server setup until EF migrations are generated on a machine with `dotnet-ef`.

## Scripts

1. `001_policy_store_sqlserver.sql`
2. `002_cache_store_sqlite.sql`

## Later conversion to EF migrations

On a machine with `dotnet-ef` available:

```powershell
dotnet ef migrations add InitialPolicyStore --project src/gateway/Zta.Gateway --context PolicyDbContext --output-dir Data/Migrations/Policy
dotnet ef migrations add InitialCacheStore --project src/gateway/Zta.Gateway --context CacheDbContext --output-dir Data/Migrations/Cache
```
