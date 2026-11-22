# TaskMan

Forum-style task management. Tasks live in forums, carry a Markdown body, attachments, an assignee, status, priority, tag, and due date, and are discussed through replies.

**Current state:** Phase 4 replies and attachments. The API migrates PostgreSQL on startup, seeds the first administrator, assigns task numbers from a database sequence, and serves cookie-authenticated auth, user, forum, task, reply, and attachment endpoints. The Vue SPA arrives in Phase 5.

The full design is in [PLAN.md](PLAN.md).

## Stack

- .NET 10 / ASP.NET Core controllers
- Entity Framework Core 10 with PostgreSQL 17 and Npgsql
- Vue 3 + TypeScript + Vite (PrimeVue and Pinia in Phase 5)
- Nginx + Docker Compose

## Prerequisites

- .NET 10 SDK (the repository pins `10.0.404` in `global.json`)
- Node.js 24 and npm
- Docker with Compose (also used by the integration tests through Testcontainers)

If the SDK was installed into `~/.dotnet`, add it to the current shell:

```bash
export PATH="$HOME/.dotnet:$PATH"
```

## Local development

Start a development PostgreSQL instance. The override publishes the database on `127.0.0.1:5432` only:

```bash
docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d db
```

Start the API with a connection string:

```bash
export Database__ConnectionString="Host=localhost;Port=5432;Database=taskman;Username=taskman;Password=change-me"
dotnet run --project src/TaskMan.Api
```

`launchSettings.json` supplies the development admin credentials (`admin` / `taskman-dev-password`) and `appsettings.Development.json` stores attachments under `data/` in the repository. On the first run against an empty database the API applies the migration and seeds the administrator.

The API listens on `http://localhost:5000`; verify database, storage, and the seeded admin with:

```bash
curl http://localhost:5000/health
```

In a second terminal, start the SPA:

```bash
npm --prefix web install
npm --prefix web run dev
```

Open `http://localhost:5173`. Vite proxies `/health` and `/api` to the API on port 5000, so cookies work without CORS configuration.

## Authentication and users

There is no public registration. An administrator creates accounts through `/api/users`.

Sessions use the `tm_session` cookie: `HttpOnly`, `SameSite=Strict`, `Path=/`, and `Secure` when `AUTH__COOKIESECURE=true`. Sessions slide for 8 hours and expire absolutely after 7 days. Changing a password, resetting a password, changing a role, or deactivating a user rotates the security stamp and invalidates existing sessions immediately. Data Protection keys are stored under `Storage__KeysPath`, so sessions survive an API restart.

Every non-GET `/api` request must include the CSRF header `X-Requested-With: TaskMan`; requests without it receive `400 application/problem+json`. Login is rate limited to 10 requests per minute per client IP and returns `429` with `Retry-After` when exceeded. Unauthenticated and forbidden API requests return `401`/`403` ProblemDetails instead of HTML redirects.

```bash
curl -c cookies.txt \
  -H 'Content-Type: application/json' \
  -H 'X-Requested-With: TaskMan' \
  -d '{"userName":"admin","password":"taskman-dev-password"}' \
  http://localhost:5000/api/auth/login

curl -b cookies.txt http://localhost:5000/api/auth/me
```

| Endpoint | Access | Purpose |
|---|---|---|
| `POST /api/auth/login` | anonymous | Start a session |
| `POST /api/auth/logout` | authenticated | End the session |
| `GET /api/auth/me` | authenticated | Current profile |
| `POST /api/auth/change-password` | authenticated | Change own password |
| `GET /api/users` | admin | Search/filter/paginate users |
| `POST /api/users` | admin | Create a user |
| `GET /api/users/{id}` | admin | Get a user |
| `PUT /api/users/{id}` | admin | Update display name, role, active flag |
| `DELETE /api/users/{id}` | admin | Deactivate a user |
| `POST /api/users/{id}/reset-password` | admin | Reset a password |
| `GET /api/users/assignable` | authenticated | Active users for assignment |

Administrators cannot demote or deactivate themselves, and at least one active administrator must remain. Password rules are 8–128 characters; usernames are 3–32 characters from `[A-Za-z0-9._-]`. Deleting a user deactivates the account rather than removing its history.

## Forums and tasks

Forums are ordered boards that contain tasks. Administrators create and edit forums; any authenticated user can read them and create tasks in active forums. Archived forums remain visible with their tasks but reject new and moved-in tasks. A forum can only be deleted when it contains no tasks.

Task permissions follow the API matrix: authors and administrators can edit or delete a task; the author, assignee, or an administrator can change only its status. Assignment changes are part of a full edit and must target an active user. Task numbers come from a database sequence and are displayed as `T-{Number}`.

| Endpoint | Access | Purpose |
|---|---|---|
| `GET /api/forums` | authenticated | Ordered forums with task counts; pass `includeArchived=false` to hide archived |
| `GET /api/forums/{idOrSlug}` | authenticated | Forum detail |
| `POST /api/forums` | admin | Create a forum |
| `PUT /api/forums/{id}` | admin | Update name, slug, description, order, archive flag |
| `DELETE /api/forums/{id}` | admin | Delete an empty forum |
| `GET /api/tasks` | authenticated | Global task list |
| `GET /api/forums/{forumId}/tasks` | authenticated | Forum-scoped task list |
| `POST /api/tasks` | authenticated | Create a task |
| `GET /api/tasks/{id}` | authenticated | Task detail with attachment metadata |
| `PUT /api/tasks/{id}` | author/admin | Full task update, including forum moves |
| `PATCH /api/tasks/{id}/status` | author/assignee/admin | Status-only update |
| `DELETE /api/tasks/{id}` | author/admin | Delete a task, its replies, and their attachment files |

List filters are `status` and `priority` (repeatable), `assigneeId` (a user ID or `me`), `tag`, `q` (case-insensitive title/body/tag search), `dueBefore`, `dueAfter`, and `overdue`. Pagination uses `page` and `pageSize` (default 25, maximum 100). Sort with `sort=activity` (default), `created`, `due`, or `priority`.

```bash
curl -b cookies.txt -H 'Content-Type: application/json' \
  -H 'X-Requested-With: TaskMan' \
  -d '{"name":"General","slug":"general","description":"General work","sortOrder":0}' \
  http://localhost:5000/api/forums

curl -b cookies.txt \
  'http://localhost:5000/api/tasks?status=Open&priority=High&sort=activity'
```

Swagger UI is enabled in Development or when `SWAGGER__ENABLED=true`, at `http://localhost:5000/swagger` (or `/swagger/` through Nginx).

## Replies and attachments

Replies are flat, chronological discussions on a task. Any authenticated user can reply to any task, including tasks in archived forums, and can edit or delete their own replies; administrators can edit or delete any reply. Creating a reply updates the task's activity timestamp. Reply lists paginate 50 per page.

Attachments are stored on disk with their metadata in the database. Any authenticated user may upload to any task or reply, up to 25 MB per file and 10 attachments per task or reply. Deleting an attachment is limited to the uploader, the author of the parent task or reply, or an administrator. Deleting a task or reply removes its attachment rows and files.

| Endpoint | Access | Purpose |
|---|---|---|
| `GET /api/tasks/{taskId}/replies` | authenticated | Paged replies, chronological (50 per page) |
| `POST /api/tasks/{taskId}/replies` | authenticated | Create a reply; updates task activity |
| `PUT /api/replies/{id}` | reply author/admin | Edit the reply body |
| `DELETE /api/replies/{id}` | reply author/admin | Delete a reply and its attachments |
| `POST /api/tasks/{taskId}/attachments` | authenticated | Upload one file (multipart) |
| `POST /api/replies/{replyId}/attachments` | authenticated | Upload one file (multipart) |
| `GET /api/attachments/{id}` | authenticated | Stream the file from disk; add `?inline=1` for raster images |
| `DELETE /api/attachments/{id}` | uploader/parent author/admin | Delete the row and the file |

Downloads are served with `Content-Disposition: attachment` (or `inline` when inline serving applies), `X-Content-Type-Options: nosniff`, and an RFC 5987-encoded filename. Inline serving is limited to PNG, JPEG, GIF, and WebP, and the file extension must match the stored content type.

```bash
curl -b cookies.txt \
  -H 'X-Requested-With: TaskMan' \
  -F 'file=@notes.txt' \
  http://localhost:5000/api/tasks/TASK_ID/attachments

curl -b cookies.txt \
  http://localhost:5000/api/tasks/TASK_ID/replies
```

## Database and migrations

The API takes a PostgreSQL advisory lock, applies pending migrations, and seeds the first administrator when `Users` is empty. There is one migrations set in `src/TaskMan.Infrastructure/Persistence/Migrations`.

To add a migration during development:

```bash
dotnet tool restore
dotnet ef migrations add <Name> \
  --project src/TaskMan.Infrastructure \
  --startup-project src/TaskMan.Api \
  --output-dir Persistence/Migrations
```

Task numbers come from the `task_number_seq` sequence and are displayed as `T-{Number}`. Gaps are expected after deletes or failed inserts.

## Build and test

```bash
dotnet build TaskMan.sln
dotnet test TaskMan.sln

npm --prefix web run type-check
npm --prefix web run build
```

The backend tests start a throwaway PostgreSQL 17 container, so Docker must be running.

## Docker

```bash
cp .env.example .env
# edit .env and replace every placeholder value
docker compose up --build
```

Open `http://localhost:8080`. Set `WEB_PORT` to change the exposed port. PostgreSQL data, attachment files, and Data Protection keys live in named volumes; `docker compose down -v` removes them.

## Configuration

`.env.example` documents the expected environment variables. The main groups are:

- `POSTGRES_*` and `Database__ConnectionString`
- `Storage__AttachmentsPath` and `Storage__KeysPath`
- `ADMIN_USERNAME`, `ADMIN_PASSWORD`, `ADMIN_DISPLAY_NAME`
- `AUTH__COOKIESECURE`, `Auth__SessionIdleHours`, `Auth__SessionAbsoluteDays`
- `Auth__LoginPermitLimit`, `Auth__LoginWindowSeconds`
- `SWAGGER__ENABLED`, `WEB_PORT`, `ASPNETCORE_ENVIRONMENT`
- `ForwardedHeaders__KnownProxies` / `ForwardedHeaders__KnownNetworks`

The first administrator is seeded only when the `Users` table is empty. If it is empty and `ADMIN_PASSWORD` is missing or shorter than 8 characters, startup fails instead of creating an insecure account. Never commit a real `.env`.

## Layout

```text
src/TaskMan.Domain/           entities and enums
src/TaskMan.Infrastructure/   EF Core, migrations, seeding, auth, file storage
src/TaskMan.Api/              controllers, DTOs, middleware
tests/TaskMan.Tests/          integration and unit tests
web/                          Vue 3 + TypeScript SPA
docker/                       container and Nginx configuration
```
