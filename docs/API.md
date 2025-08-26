
# API (summary)
- `GET /api/resources?q=&tier=&type=&biome=&status=&page=&sort=`
- `POST /api/resources` (public) — creates as Unconfirmed
- `POST /api/resources/check-duplicate` (public) — returns likely matches
- `POST /api/resources/{id}/image` (public) — upload, resize/transcode, store
- `PATCH /api/resources/{id}` (admin)
- `DELETE /api/resources/{id}` (admin)
- `POST /api/resources/{id}/status` (admin) — confirm/unconfirm
- `POST /api/resources/bulk-import?mode=dryRun|commit` (admin)

Auth for admin endpoints: ASP.NET Core Identity (cookie) + `[Authorize(Roles="Admin")]`.
