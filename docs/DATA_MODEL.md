
# Data model (EF Core)

## Entities
**Resource**
- Id (Guid), Tier (1–10), Name, CanonicalName, Status (Confirmed|Unconfirmed)
- TypeId (FK), BiomeId (FK)
- Img256Url, Img512Url, ImagePhash (nullable)
- CreatedAt, UpdatedAt
- CreatedById, UpdatedById (nullable)
- SubmitterIp (nullable), SubmitterUserAgent (nullable)
- Unique: (Tier, TypeId, BiomeId, CanonicalName)

**Type**, **Biome**
- Id (Guid), Name (unique), Slug (unique), IsActive

**ResourceAlias**
- Id (Guid), ResourceId (FK), Alias, CanonicalAlias
- Unique: (ResourceId, CanonicalAlias)

**User** (Identity: Admin only)
- Id, Email (unique)

**AuditLog**
- Id, ActorId (nullable), ActorLabel, Action, SubjectTable, SubjectId, Diff (json), CreatedAt

## Notes
- CanonicalName strips accents, lowercases, trims, and collapses whitespace.
- Consider enabling Postgres `pg_trgm` for fuzzy search on CanonicalName/Name.
