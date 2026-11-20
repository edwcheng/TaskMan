# TaskMan — Project Plan

**Status:** Approved (v2.1)  
**Date:** 19 November 2026  
**Delivery status:** Phase 0 complete · Phase 1 complete · Phase 2 complete · Phase 3 complete · Phase 4 next  
**Target:** Forum-style task management application

### Revision summary (v1 → v2)

| Change | Reason |
|---|---|
| JWT + refresh tokens replaced by server-side cookie sessions | Same-origin SPA, no SSO/mobile/API clients; removes token rotation, refresh table, Axios refresh interceptor; instant revocation |
| SQL Server support removed; PostgreSQL only | Removes provider switching, model cache key, second migrations assembly, cascade-path workaround |
| Linux only (Docker Compose) | Windows Server 2012 deployment dropped |
| Attachments stored on disk (Docker volume), metadata in PostgreSQL | No memory buffering of large `bytea`, simple streaming, smaller database |
| Pinia reduced to `auth`, `ui`, `forums`; list filters live in the URL | Shareable links, working back button, less state to keep consistent |
| Added: forwarded-headers config, Data Protection key persistence, migration lock, Playwright smoke test | Gaps found in review |

### Review fixes (v2.1)

- Backups now quiesce writes by default (stop the API) so the dump and file copy are consistent; snapshot mode remains documented for zero-downtime environments.
- The 7-day absolute session limit is explicitly enforced alongside the 8-hour sliding window.
- Inline attachment serving is restricted to raster images; SVG and other script-capable types are excluded.
- Proxy trust is configured explicitly instead of relying on container IPs.
- Named-volume ownership is initialized in the image and checked at startup.
- Task moves into archived forums are rejected.
- The upload-permission open question is resolved: any authenticated user may upload; deletion remains restricted.

---

## 1. Overview

TaskMan is a self-hosted, forum-style task tracker. Tasks live inside forums (boards), each with a Markdown body, attachments, an assignee, status, priority, tag, and due date. Users discuss tasks through flat chronological replies. Normal users can create tasks and replies and manage their own content; administrators manage users, forums, and all content.

The application ships as a Docker Compose stack with Nginx, the .NET 10 API, and PostgreSQL. A seeded administrator bootstraps the system; there is no public registration.

## 2. Goals

- Provide a clear, modern board → task → discussion workflow.
- Preserve Markdown and pasted source code exactly, with safe rendered previews.
- Enforce a simple, understandable permission model server-side.
- Store attachment bytes on a dedicated disk volume, with metadata in the database.
- Use PostgreSQL on Linux as the single supported platform.
- Deliver a reproducible Docker/Nginx deployment plus a fast local development workflow.
- Keep the codebase small enough to maintain: pragmatic layering, explicit decisions, integration tests for critical behavior.

## 3. Non-goals (v1)

- Notifications, @mentions, email delivery, or real-time updates.
- Public registration, self-service password reset, OAuth/SSO, or multi-factor authentication.
- Per-forum membership or granular ACLs.
- Full-text search engines, saved filters, or cross-task reporting.
- Soft-delete/trash, audit logs, or activity feeds.
- Antivirus scanning of uploaded files.
- SQL Server or SQLite support; Windows hosting.
- Public/third-party API clients and native mobile apps (the web UI is responsive).
- Running multiple API instances (single instance assumed; see section 8).

## 4. Key decisions

| Area | Decision |
|---|---|
| Runtime | .NET 10 (LTS), Linux containers |
| ORM | EF Core 10 with Npgsql |
| Database | PostgreSQL 17 only |
| Auth | ASP.NET Core cookie authentication (server-validated session) |
| Session cookie | `tm_session`: HttpOnly, SameSite=Strict, Path=/, Secure via config; sliding 8 h and absolute 7 days, both enforced server-side |
| Session validation | Per request, check user is active and `SecurityStamp` matches (indexed PK lookup) |
| CSRF defence | SameSite=Strict plus required `X-Requested-With: TaskMan` header on all non-GET `/api` requests |
| Accounts | Seeded admin; admins create all accounts; deactivation instead of hard delete |
| Assignee | Single optional assignee; author/admin assigns; assignee changes status |
| Replies | Flat, chronological, paginated |
| Task references | Database sequence, displayed as `T-142` |
| Attachments | Files on disk (`/data/attachments`, Docker volume); metadata in DB; 25 MB per file, 10 per task/reply, one file per upload request |
| Markdown | Plain textarea editor, `markdown-it` + `highlight.js`, raw HTML disabled, DOMPurify |
| Frontend | Vue 3 + TypeScript + Vite + PrimeVue 4 + Pinia (3 small stores) + Vue Router |
| Theme | PrimeVue Aura preset, indigo primary, slate surfaces, light/dark |
| Deployment | Docker Compose, Nginx SPA + reverse proxy, HTTP by default, optional TLS override |
| Dev workflow | Local .NET 10 SDK + Vite dev server; Docker used for integration checks |

## 5. Architecture

```
Browser
  │
  ▼
Nginx (web container)
  ├── /            → Vue SPA (static assets, SPA fallback)
  └── /api, /health → reverse proxy → API container
                                      │
                       ┌──────────────┴──────────────┐
                       ▼                             ▼
                  PostgreSQL                 /data/attachments (volume)
                                             /data/keys (Data Protection keys)
```

- The API is a single instance. Durable state lives in PostgreSQL (data) and two volumes (attachments and Data Protection keys).
- Nginx and the API share a Docker network. Only Nginx is exposed.
- The session cookie is same-origin in production and through the Vite proxy in development, so no cross-site cookie or CORS configuration is required.
- Because the browser sends the cookie automatically, attachment downloads and inline images in Markdown can use plain URLs (`/api/attachments/{id}`) without blob requests.
- Swagger UI is served by the API and proxied by Nginx when enabled.

## 6. Repository layout

```
TaskMan.sln
Directory.Build.props
Directory.Packages.props
.editorconfig
.gitignore
.env.example
docker-compose.yml
docker-compose.tls.yml
PLAN.md
README.md

src/
  TaskMan.Domain/
    Entities/
    Enums/
    Authorization/
  TaskMan.Infrastructure/
    Persistence/               (DbContext, configurations, Migrations/)
    Auth/
    Files/                     (IAttachmentStore, disk implementation)
    Seed/
  TaskMan.Api/
    Controllers/
    Contracts/
    Mapping/
    Middleware/
    Program.cs
    appsettings.json
    appsettings.Development.json

tests/
  TaskMan.Tests/
    Auth/  Forums/  Tasks/  Replies/  Attachments/  Infrastructure/

web/
  src/
    api/  assets/  components/  layouts/  router/  stores/  views/  utils/
  e2e/                         (Playwright smoke test)
  public/
  index.html
  package.json
  vite.config.ts
  tsconfig.json

docker/
  api.Dockerfile
  web.Dockerfile
  nginx/default.conf
  nginx/tls.conf.template

scripts/
  backup.sh
  restore.sh
```

## 7. Domain model

All primary keys are GUIDs generated with `Guid.CreateVersion7()`. All timestamps are stored in UTC. Enums are persisted as readable strings.

### User

| Field | Type | Notes |
|---|---|---|
| Id | Guid | PK |
| UserName | varchar(32) | Unique, case-insensitive via normalized column |
| DisplayName | varchar(100) | Shown throughout the UI |
| PasswordHash | text | ASP.NET Core `PasswordHasher<User>` |
| SecurityStamp | Guid | Rotated on password change/reset, role change, deactivation; invalidates all sessions |
| Role | varchar(16) | `Admin` or `User` |
| IsActive | bool | Deactivated users cannot log in; content remains attributed |
| CreatedAt | timestamptz | |
| LastLoginAt | timestamptz? | Updated on successful login |

### Forum

| Field | Type | Notes |
|---|---|---|
| Id | Guid | PK |
| Name | varchar(100) | |
| Slug | varchar(100) | Unique, generated from name, editable |
| Description | varchar(500)? | Optional |
| SortOrder | int | Admin-controlled ordering |
| IsArchived | bool | Read-only presentation; no new tasks |
| CreatedAt | timestamptz | |

Archived forums remain visible with a badge and cannot receive new tasks. A forum can only be deleted when it contains no tasks.

### Task

| Field | Type | Notes |
|---|---|---|
| Id | Guid | PK |
| Number | bigint | Global sequence, unique; displayed as `T-{Number}` |
| ForumId | Guid | FK → Forum, restricted |
| Title | varchar(200) | |
| Body | text | Raw Markdown, max 100,000 characters |
| Status | varchar(20) | `Open`, `InProgress`, `Completed`, `Closed`, `OnHold` |
| Priority | varchar(16) | `Low`, `Normal`, `High`, `Urgent` |
| Tag | varchar(50)? | Free text project/build label |
| DueDate | date? | Date-only, UTC-independent |
| AuthorId | Guid | FK → User, restricted |
| AssigneeId | Guid? | FK → User, restricted |
| CreatedAt | timestamptz | |
| UpdatedAt | timestamptz | Content edits |
| LastActivityAt | timestamptz | Task edits, status changes, and new replies; default sort |

Indexes: `(ForumId, LastActivityAt)`, `(Status, LastActivityAt)`, `(AssigneeId, Status)`, `DueDate`, `Tag`, unique `Number`.

Status transitions are unrestricted for permitted actors (any status to any status).

### Reply

| Field | Type | Notes |
|---|---|---|
| Id | Guid | PK |
| TaskId | Guid | FK → Task, cascade |
| AuthorId | Guid | FK → User, restricted |
| Body | text | Raw Markdown, max 100,000 characters |
| CreatedAt | timestamptz | |
| UpdatedAt | timestamptz | Enables an "edited" indicator |

Index: `(TaskId, CreatedAt)`.

### Attachment (metadata only)

| Field | Type | Notes |
|---|---|---|
| Id | Guid | PK; also the on-disk file name |
| TaskId | Guid? | Exactly one of TaskId/ReplyId is set |
| ReplyId | Guid? | |
| FileName | varchar(255) | Original name, sanitized with `Path.GetFileName`; never used in a filesystem path |
| ContentType | varchar(150) | Client-provided; never trusted for rendering |
| SizeBytes | bigint | Server-verified (bytes actually written) |
| UploadedById | Guid | FK → User, restricted |
| CreatedAt | timestamptz | |

A check constraint enforces exactly one parent. With PostgreSQL only, attachment FKs to Task/Reply can use normal cascade for the metadata rows; the service still deletes the files on disk (see section 9).

## 8. Database and storage strategy

### Database

- PostgreSQL 17 is the only supported database. Connection string via `Database__ConnectionString`.
- A single migrations set lives in `TaskMan.Infrastructure/Persistence/Migrations`.
- On startup, the API takes a PostgreSQL advisory lock, applies pending migrations with retry (10 attempts, 3-second delay), releases the lock, and then seeds the admin if the `Users` table is empty. The lock keeps an accidental second instance from racing migrations.
- Task numbers use a database sequence `task_number_seq` with `nextval('task_number_seq')` as the column default. Gaps are acceptable.

### Attachment storage

- Files are stored under `Storage:AttachmentsPath` (default `/data/attachments`), a named Docker volume `attachments`.
- The on-disk name is the attachment GUID, sharded by the first two characters (for example `/data/attachments/01/0192…`). The original file name lives only in the database.
- Upload flow: stream the request to a temp file in the same volume while counting bytes and enforcing the 25 MB limit → move it to its final name → insert the metadata row. If the insert fails, delete the file.
- Delete flow: delete the database row(s) first, then remove the files. A failed file removal leaves an orphan file, which is harmless. Backups must quiesce writes while copying rows and files (section 13).
- Deleting a task or reply collects its attachment IDs first, deletes the rows in one transaction, then removes the files.
- Downloads stream with `PhysicalFile`/`FileStreamResult`, no buffering in memory. Missing files return 404 and are logged.
- An optional hosted job (daily, disabled by default) lists files with no matching row and reports them; deletion requires an explicit setting.
- `IAttachmentStore` abstracts the disk implementation so object storage could replace it later.

## 9. Security and authentication

### Sessions

- `POST /api/auth/login` validates username/password, rejects inactive users, records `LastLoginAt`, signs the user in with cookie authentication, and returns the user profile. Claims carry user ID, `SecurityStamp`, and a `session_started` timestamp.
- On every authenticated request, `OnValidatePrincipal` loads the user by primary key and rejects the session if the user is inactive, the role changed, or the stamp differs. Role and deactivation changes therefore take effect immediately.
- `POST /api/auth/logout` clears the cookie for the current browser. "Sign out everywhere" is achieved by rotating `SecurityStamp` (done automatically on password change/reset, role change, and deactivation).
- API calls without a valid session return 401 (no redirects); the SPA redirects to login with a return URL.
- Sliding expiration extends the 8-hour idle window on activity. The 7-day absolute limit is enforced by checking the `session_started` claim during principal validation; `IssuedUtc` alone is unsuitable because it is reset when the sliding window renews. Rejected sessions force a new login.
- ASP.NET Core Data Protection keys are persisted to the `/data/keys` volume. Without this, every container restart would invalidate all sessions.
- The SPA calls `GET /api/auth/me` on startup to restore the session; there is no token handling in JavaScript.

### CSRF

- Cookie is `SameSite=Strict`, and all non-GET `/api` requests must carry `X-Requested-With: TaskMan`; requests without it return 400. The Axios instance sets the header globally. Multipart uploads set it too.
- GET endpoints never change state.

### Passwords and accounts

- Passwords are hashed with ASP.NET Core's `PasswordHasher<User>` (PBKDF2, framework defaults).
- Minimum length 8; maximum 128; username 3–32 characters (`[A-Za-z0-9._-]`).
- Admin-created accounts receive an initial password; the admin can reset passwords later.
- Users can change their own password by supplying the current password; this rotates their `SecurityStamp` and re-issues the current session.
- Admins cannot deactivate or demote their own account, and the last active admin cannot be removed.
- Deleting a user through the API deactivates them; accounts are never hard-deleted.

### API hardening

- Login is rate-limited (10 requests/minute/IP). 429 responses include `Retry-After`.
- The API trusts `X-Forwarded-For`/`X-Forwarded-Proto` only from explicitly configured proxies (`ForwardedHeaders__KnownProxies` or `ForwardedHeaders__KnownNetworks`, defaulting to the Compose network CIDR). Container IPs are not stable, so this is configuration rather than a hardcoded address. Without it, rate limiting would see every user as Nginx's IP.
- CORS is not needed in production or with the Vite proxy; no wildcard origins are configured.
- Markdown is sanitized in the browser; raw HTML is disabled at render time.
- Downloads are always served with `Content-Disposition: attachment` and `X-Content-Type-Options: nosniff`. Images referenced from Markdown may use an explicit `?inline=1` option restricted to a fixed raster-image allowlist (PNG, JPEG, GIF, WebP). SVG and other script-capable types are never served inline; the effective type is derived from validated extension and content type, never trusted from the client alone.
- Nginx adds `X-Content-Type-Options`, `Referrer-Policy`, and `X-Frame-Options: DENY`; HSTS is added only in the TLS configuration.
- Upload size is capped in ASP.NET Core and Nginx; filenames are sanitized and RFC 5987-encoded in headers.
- Plain HTTP is the default for trusted LANs. Passwords and session cookies are then readable on the network; the README states this prominently and documents the TLS override.

## 10. Permission matrix

| Action | User | Author | Assignee | Admin |
|---|---|---|---|---|
| View forums, tasks, replies, attachments | ✓ | ✓ | ✓ | ✓ |
| Create task in an active forum | ✓ | ✓ | ✓ | ✓ |
| Upload attachment to any task/reply | ✓ | ✓ | ✓ | ✓ |
| Edit all task fields | — | ✓ | — | ✓ |
| Change task status only | — | ✓ | ✓ | ✓ |
| Delete task (cascades replies, attachments) | — | ✓ | — | ✓ |
| Create reply | ✓ | ✓ | ✓ | ✓ |
| Edit/delete own reply | ✓ | — | — | ✓ any |
| Delete attachment | uploader | parent author | — | ✓ any |
| Create/edit/archive/delete empty forum | — | — | — | ✓ |
| Manage users, reset passwords | — | — | — | ✓ |

- The frontend hides unavailable actions, but the API is the authority and returns 403 consistently.
- Assignment changes require author or admin. Assignees cannot reassign.
- Moving a task is an edit action (author/admin) and is allowed only into an active forum; archived forums reject both new and moved-in tasks.
- Only active users appear in the assignee lookup.
- Any authenticated user may upload an attachment to any task or reply (intentional, forum-style collaboration). Deletion remains limited to the uploader, the parent author, or an admin.

## 11. API design

Base path `/api`; JSON with camelCase; errors use RFC 7807 `ProblemDetails`; list responses use `{ items, page, pageSize, totalCount, totalPages }`.

### Auth

| Method | Route | Access | Purpose |
|---|---|---|---|
| POST | `/auth/login` | anonymous | Authenticate, set session cookie |
| POST | `/auth/logout` | authenticated | Clear session cookie |
| GET | `/auth/me` | authenticated | Current user profile |
| POST | `/auth/change-password` | authenticated | Change own password |

### Users

| Method | Route | Access | Purpose |
|---|---|---|---|
| GET | `/users` | admin | Paged user list with search/filter |
| POST | `/users` | admin | Create account |
| GET | `/users/{id}` | admin | User detail |
| PUT | `/users/{id}` | admin | Update display name, role, active flag |
| DELETE | `/users/{id}` | admin | Deactivate account |
| POST | `/users/{id}/reset-password` | admin | Set a new password, invalidate sessions |
| GET | `/users/assignable` | authenticated | Id, username, display name of active users |

### Forums

| Method | Route | Access | Purpose |
|---|---|---|---|
| GET | `/forums` | authenticated | Ordered list with counts; optional `includeArchived` |
| GET | `/forums/{idOrSlug}` | authenticated | Forum detail |
| POST | `/forums` | admin | Create |
| PUT | `/forums/{id}` | admin | Update name, slug, description, order, archive flag |
| DELETE | `/forums/{id}` | admin | Delete only when empty |

### Tasks

| Method | Route | Access | Purpose |
|---|---|---|---|
| GET | `/tasks` | authenticated | Global list with filters |
| GET | `/forums/{forumId}/tasks` | authenticated | Forum-scoped list |
| POST | `/tasks` | authenticated | Create |
| GET | `/tasks/{id}` | authenticated | Detail including attachments |
| PUT | `/tasks/{id}` | author/admin | Full update |
| PATCH | `/tasks/{id}/status` | author/assignee/admin | Status-only update |
| DELETE | `/tasks/{id}` | author/admin | Delete with cascade |

List query parameters: `status` (repeatable), `priority` (repeatable), `assigneeId` (or `me`), `tag`, `q`, `dueBefore`, `dueAfter`, `overdue`, `page` (default 1), `pageSize` (default 25, max 100), `sort` (`activity` default, `created`, `due`, `priority`).

Search is case-insensitive contains over title, body, and tag (`ILIKE`). Priority sort uses an explicit rank (Urgent > High > Normal > Low) translated to a `CASE` expression. Default list order is `LastActivityAt` descending.

### Replies

| Method | Route | Access | Purpose |
|---|---|---|---|
| GET | `/tasks/{taskId}/replies` | authenticated | Paged, chronological (50/page) |
| POST | `/tasks/{taskId}/replies` | authenticated | Create; touches task activity |
| PUT | `/replies/{id}` | reply author/admin | Edit body |
| DELETE | `/replies/{id}` | reply author/admin | Delete with attachments |

### Attachments

| Method | Route | Access | Purpose |
|---|---|---|---|
| POST | `/tasks/{taskId}/attachments` | authenticated | Upload one file (multipart, streamed to disk) |
| POST | `/replies/{replyId}/attachments` | authenticated | Upload one file (multipart, streamed to disk) |
| GET | `/attachments/{id}` | authenticated | Stream file from disk |
| DELETE | `/attachments/{id}` | uploader/parent author/admin | Delete |

Limits are enforced server-side: 25 MB per file, 10 attachments per parent. Task/reply DTOs contain attachment metadata only. Downloads use the plain URL and are authorized by the session cookie; inline rendering is limited to the raster-image allowlist in section 9.

### Operational

| Method | Route | Purpose |
|---|---|---|
| GET | `/health` | Liveness plus database connectivity and writable attachments directory |
| GET | `/swagger` | Swagger UI when enabled |

## 12. Frontend design

### Routes

| Route | View | Guard |
|---|---|---|
| `/login` | Login | Anonymous |
| `/` | Redirect to `/tasks` | Auth |
| `/tasks` | All tasks with filters | Auth |
| `/forums/:slug` | Forum task list | Auth |
| `/my-tasks` | Assigned to me / authored by me tabs | Auth |
| `/tasks/new` | Create task | Auth |
| `/tasks/:id` | Task detail with replies | Auth |
| `/tasks/:id/edit` | Edit task | Auth + author/admin |
| `/admin/users` | User management | Admin |
| `/admin/forums` | Forum management | Admin |
| `/profile` | Display name, password, theme | Auth |
| `/:pathMatch(.*)*` | Not found | — |

### Layout and components

- App shell: top bar (logo, global search, theme toggle, user menu), collapsible forum sidebar, main content area, toast outlet.
- Desktop: task table with status/priority tags, assignee avatars, due-date emphasis, reply/attachment counts.
- Mobile: card list; filters in a drawer.
- Key components: `TaskFilters`, `TaskTable`, `TaskCard`, `StatusBadge`, `PriorityBadge`, `UserAvatar`, `MarkdownEditor`, `MarkdownPreview`, `AttachmentUploader`, `AttachmentList`, `ReplyList`, `ReplyComposer`, `ConfirmDialog`, `EmptyState`.
- PrimeVue components: Button, InputText, Textarea, Select, MultiSelect, DatePicker, Tag, DataTable, Pagination, Dialog, ConfirmDialog, FileUpload, Menu, Avatar, Skeleton, Message, Drawer.
- Status colors: Open blue, In Progress amber, Completed green, Closed gray, On Hold purple. Priority colors: Low gray, Normal blue, High orange, Urgent red.
- Theme: `definePreset(Aura, { semantic: { primary: indigo, colorScheme: slate } })`; dark mode via `.p-dark` on `<html>`; preference persisted in `localStorage`.
- Accessibility: labeled controls, visible focus states, keyboard-operable dialogs, contrast checked for light/dark.

### Markdown editor

- Plain `<textarea>` with a formatting toolbar and Write/Preview tabs. No `contenteditable`, no auto-formatting, no transformation of pasted content.
- Toolbar actions: heading, bold, italic, strikethrough, inline code, fenced code block, link, bullet list, numbered list, quote, horizontal rule.
- Preview uses `markdown-it` with `html: false`, `linkify: true`; fenced blocks are highlighted with `highlight.js`; output is passed through DOMPurify; external links open in a new tab with `rel="noopener noreferrer"`.
- Handles long bodies (up to 100,000 characters) with multiple paragraphs; preview scrolls within the page.
- Unsaved-changes guard on navigation; server errors surface inline and via toast.

### State and data flow

- Pinia stores (three, kept small):
  - `auth`: current user, bootstrap (`/auth/me`), login/logout, permission getters.
  - `forums`: forum list for the sidebar and pickers; refreshed after admin changes.
  - `ui`: theme, sidebar state.
- Task list filters, search text, sort, and page live in the URL query string, not in a store. Views read the query, fetch their data, and re-fetch on query change. This gives shareable links and a working back button.
- `api/` contains the Axios instance (sets `X-Requested-With`, redirects to login on 401) and typed endpoint modules.
- Pinia getters derive UI permissions; the server remains authoritative.
- Loading skeletons for lists and detail; empty states with a clear call to action; consistent toast handling for mutations.

## 13. Infrastructure and deployment

### Containers

- `api`: multi-stage .NET 10 SDK → ASP.NET runtime, non-root user, listens on 8080, health check against `/health`. Mounts volumes `attachments` (`/data/attachments`) and `keys` (`/data/keys`). The image creates both directories owned by the runtime user before dropping privileges, so first-use named volumes inherit the correct ownership; startup verifies writability and fails fast otherwise.
- `web`: Node 24 build stage → Nginx alpine, serves the SPA and proxies `/api`, `/swagger`, and `/health` to `api:8080`, passing `X-Forwarded-For` and `X-Forwarded-Proto`.
- `db`: PostgreSQL 17 alpine with named volume `pgdata` and a `pg_isready` health check.
- Compose starts `db` first (healthy), then `api`, then `web`; default exposed port `8080` (`WEB_PORT` overridable).

### Nginx

- `try_files $uri $uri/ /index.html` for SPA routing.
- `client_max_body_size 30m`; `proxy_request_buffering off` for uploads.
- Gzip for text assets; long-lived cache headers for hashed `/assets/`.
- Security headers as described in section 9.
- HTTP config by default; `docker-compose.tls.yml` swaps in a TLS template with port 443, HTTP→HTTPS redirect, HSTS, and mounted `certs/fullchain.pem` + `certs/privkey.pem`.

### Configuration

`.env.example` documents:

- `POSTGRES_*` and `Database__ConnectionString`
- `Storage__AttachmentsPath`, `Storage__KeysPath`
- `ADMIN_USERNAME`, `ADMIN_PASSWORD`, `ADMIN_DISPLAY_NAME`
- `AUTH__COOKIESECURE`, `SWAGGER__ENABLED`, `WEB_PORT`, `ASPNETCORE_ENVIRONMENT`
- `ForwardedHeaders__KnownProxies` or `ForwardedHeaders__KnownNetworks` (default: Compose network CIDR)

Production startup fails fast when the admin seed is required but `ADMIN_PASSWORD` is missing or too short, and when the attachments or keys directory is not writable. Swagger defaults to enabled in Development and disabled in Production.

### Backups and restores

A single documented script (`scripts/backup.sh`) produces one consistent set. Because attachment rows and bytes are deleted in separate steps, the default script quiesces writes:

1. Put the application into maintenance mode or stop the `api` container. The `db` container stays up. This is the default; the expected downtime is the duration of the dump plus the file copy.
2. `pg_dump` the database.
3. Copy (or `rsync`) the `attachments` volume.
4. Include the `keys` volume if sessions should survive a restore (optional; users can simply log in again).
5. Start the API again and verify `/health`.

The script uses a `trap` so the API is restarted even if a step fails. With no writes during steps 2–4, every attachment row in the dump is guaranteed to have its file. The copy may also contain pre-existing orphan files, which are harmless.

For deployments that cannot tolerate the pause, a filesystem snapshot mode is documented: quiesce writes only for the snapshot instant (LVM/ZFS over the database and attachments volumes), then run `pg_dump` against the snapshot. The chosen mode and its trade-offs are recorded in the README. The restore procedure runs in reverse order.

## 14. Testing and quality

### Backend

- xUnit integration tests with `WebApplicationFactory` and a Testcontainers PostgreSQL 17 instance; attachments use a temp directory per test run.
- Coverage:
  - Login success/failure, inactive users, session validation, stamp rotation invalidating sessions, absolute-expiry rejection, logout, password change, missing CSRF header rejected, forwarded-header trust.
  - Permission matrix for task edits, status-only updates, deletion, reply edits, attachment deletion.
  - Forum CRUD restrictions, archive behavior, delete-only-when-empty.
  - Task numbering, search/filter/sort/pagination, forum moves, moves into archived forums rejected.
  - Reply creation touches task activity.
  - Attachment size/count limits, parent constraint (task xor reply), file written and removed on disk, upload rollback on database failure, download headers, inline allowlist (SVG rejected).
  - Admin guards: cannot deactivate/demote self or last admin.
- Unit tests for slug generation, priority ranking, and permission helpers.

### Frontend

- Vitest + Vue Test Utils for Markdown sanitization, permission getters, 401 handling with mocked Axios, and filter ↔ query-string serialization.
- Playwright smoke test against the Compose stack: login, create forum/task, reply, upload/download attachment, status change.
- `vue-tsc --noEmit` type check and production build in the verification checklist.
- ESLint + Prettier.
- Manual checklist for items not automated: admin user/forum management, theme toggle, mobile layout, Markdown paste fidelity.

### Operations

- Backup/restore smoke test: stop the API, run `scripts/backup.sh`, restore the dump and attachments into a scratch stack, and verify that a previously uploaded attachment downloads. Confirm the API restarts when a backup step fails.

### Commands

```bash
dotnet test TaskMan.sln
npm --prefix web run type-check
npm --prefix web run test
npm --prefix web run build
npm --prefix web run e2e
docker compose up --build
```

## 15. Delivery phases

### Phase 0 — Environment and scaffold

- Install .NET 10 SDK locally (`dotnet-install.sh --channel 10.0`, `~/.dotnet`).
- Create solution, projects, package management, editor config, `.gitignore`.
- Add `/health`, base `Program.cs`, Dockerfile skeletons, Compose skeleton.
- **Done when:** solution builds, API serves `/health`, Vite dev server starts.

### Phase 1 — Data foundation

- Domain entities/enums, `AppDbContext` (Npgsql), single migrations set.
- Task-number sequence.
- Startup advisory lock, migration retry, and admin seed.
- **Done when:** a fresh PostgreSQL database migrates; seeded admin exists; `T-1` numbering works.

### Phase 2 — Auth and users

- Cookie authentication, session validation with `SecurityStamp`, Data Protection key persistence, CSRF header check, forwarded headers, login rate limiting.
- Auth endpoints and admin user endpoints; password policies and reset; last-admin guards.
- Backend tests for auth and users.
- **Done when:** login/logout and all user-management rules pass integration tests, and sessions survive an API container restart.

### Phase 3 — Forums and tasks

- Forum CRUD, ordering, archive rules, delete-only-when-empty.
- Task CRUD, numbering, filters/search/sort/pagination, permission matrix, status endpoint.
- Backend tests.
- **Done when:** permission and query integration tests pass; Swagger documents every endpoint.

### Phase 4 — Replies and attachments

- Reply CRUD, activity touch, pagination.
- `IAttachmentStore` disk implementation, streamed upload/download/delete, limits, check constraint, file cleanup on task/reply deletion.
- Backend tests including rollback and orphan handling.
- **Done when:** attachment headers/limits and delete cascades (rows and files) are verified.

### Phase 5 — Frontend foundation

- Vite/TS/PrimeVue/Pinia/Router setup, indigo/slate theme, dark mode.
- Axios client, `auth`/`forums`/`ui` stores, router guards, app shell, login.
- **Done when:** login persists across reload, guards redirect correctly, theme persists.

### Phase 6 — Core task UI

- Task lists (global/forum/my tasks) with URL-driven filters, search, pagination, sorting.
- Task create/edit with Markdown editor and attachment manager.
- Task detail with metadata sidebar, replies, attachments.
- **Done when:** full board → task → discussion flow works against the API.

### Phase 7 — Admin, profile, polish

- Admin users and forums screens; profile and password change.
- Responsive/mobile refinements, empty/loading/error states, accessibility pass.
- **Done when:** all roles can complete their workflows without dead ends.

### Phase 8 — Deployment and handover

- Finalize Dockerfiles, Nginx configs, Compose/TLS overrides, `.env.example`, `scripts/backup.sh`.
- Playwright smoke test wired into the verification checklist.
- Verify the backup script with the API quiesced and restore into a scratch stack; confirm an attachment row resolves to its file.
- README: local development, migrations, production deploy, TLS, backups and restore, troubleshooting.
- End-to-end Docker verification and final test pass.
- **Done when:** `docker compose up --build` yields a working stack from a clean checkout, and the README covers every supported mode.

## 16. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Database and attachment files drift apart | Defined write/delete order, upload rollback, orphan report job, write-quiesced backups |
| Backup captures a row without its file | Default script stops the API during dump and copy; snapshot mode documented for zero-downtime deployments |
| Backups miss attachments | Single backup script and restore doc; verified restore into a scratch stack |
| Sessions lost on restart | Persist Data Protection keys on a volume; tested in Phase 2 |
| Rate limiting sees only the proxy IP | Forwarded headers configured with an explicit proxy list; integration-tested |
| Cookie-based CSRF | SameSite=Strict plus required custom header; tests for rejection |
| Large uploads strain disk/memory | 25 MB cap, one file per request, streamed to disk, Nginx limit and unbuffered proxying |
| Markdown XSS | Raw HTML disabled, DOMPurify, external-link hardening, no `innerHTML` outside the sanitized renderer |
| Session cookie on plain HTTP | `AUTH__COOKIESECURE` switch, HTTP only for trusted LAN, Secure enforced in TLS override and production guidance |
| Accidental second API instance | Advisory lock around migrations; documented as single-instance |
| .NET 10 tooling availability | Local user-scoped SDK install and pinned `sdk:10.0` Docker images |
| PrimeVue/Vite API changes | Pin versions in `package-lock.json` and `Directory.Packages.props`; verify build early |
| Search performance at scale | Acceptable `ILIKE` search for v1; path to PostgreSQL FTS documented as future work |
| No antivirus scanning | Trusted internal deployment; forced download and `nosniff` reduce browser-side risk; note as future work |

## 17. Definition of done

- All functional requirements in the agreed scope are implemented and reachable from the UI.
- Permission rules are enforced server-side and covered by integration tests.
- Migrations apply on a fresh PostgreSQL database; Compose runs the full stack.
- Attachments persist on the volume across container restarts and are covered by a write-quiesced backup that has been restore-tested.
- Markdown paste fidelity, long bodies, code highlighting, and sanitization are verified manually.
- Docker Compose brings up the full stack from a clean checkout with a seeded admin.
- `dotnet test`, frontend type check, frontend tests, Playwright smoke test, and production build pass.
- README covers local dev, Docker deployment, TLS, migrations, backups/restore, and admin bootstrap.
- No secrets are committed; `.env.example` contains placeholders only.

## 18. Future work

- Notifications (in-app/email) and @mentions.
- Full-text search (PostgreSQL `tsvector`).
- Audit/activity log and soft delete.
- Saved filters, CSV export, dashboards.
- Per-forum permissions and team membership.
- Antivirus scanning and attachment quotas.
- Object storage behind `IAttachmentStore`.
- Automated ACME certificate issuance.
