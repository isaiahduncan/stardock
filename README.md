# Polling site: C# + Postgres (branch `postgres`)

A polling site on ASP.NET Core and Postgres. People create a poll (a title, a description and a list of choices), anyone can open a poll and submit one response by picking a choice, and polls and responses can be edited afterwards. Edits are checked against a version marker (the "ETag"), so an edit made from a stale page is rejected, not silently overwritten.

This branch swaps the Cosmos DB storage on `main` for Postgres. Everything else (pages, controllers, models) is unchanged.

**Who wrote what.** Claude wrote the scaffolding: the Postgres setup, `PollStore` (all of it, including its SQL), the models, `ResponseStore.AddAsync` and its insert statement, and the README. Isaiah wrote the rest by hand, using `PollStore` as a reference: the `responses` table definition in `PostgresBootstrap`, the SQL for listing, getting, updating and existence-checking responses, and the C# for `ResponseStore.ListAsync`, `GetAsync`, `UpdateChoiceAsync` and `ReadResponsesAsync`.

**Status: not compiled or run.** It was written without access to the .NET SDK. Run it once on your machine and fix anything that surfaces.

## Run it

```bash
docker compose -f compose.postgres.yaml up -d   # starts Postgres 16
cd src
dotnet run --urls http://localhost:5000          # connects, then serves the app
```

App: http://localhost:5000. The app retries the connection for about a minute, so starting both back to back is fine.

## Settings

| Setting | Default |
|---|---|
| `POSTGRES_CONNECTION` | `Host=localhost;Port=5432;Username=app;Password=app;Database=app` (matches `compose.postgres.yaml`) |

## Useful commands

| Command | What it does |
|---|---|
| `docker compose -f compose.postgres.yaml up -d` | Start Postgres |
| `docker compose -f compose.postgres.yaml down` | Stop it, keep the data |
| `docker compose -f compose.postgres.yaml down -v` | Stop it and wipe the data |
| `docker compose -f compose.postgres.yaml logs -f db` | Watch Postgres logs |
| `docker exec -it interview-postgres psql -U app -d app` | Open a SQL prompt (try `\dt` to list tables) |

The compose file isn't named `compose.yaml`, so every command needs `-f compose.postgres.yaml`.

## What the stores must do

| Store method | Job |
|---|---|
| `PollStore.CreateAsync` | Save a poll and its choices, return it |
| `PollStore.GetAsync` | One poll with its choices, or null (also null for an invalid id) |
| `PollStore.ListAsync` | All polls with their choices, newest first |
| `PollStore.UpdateAsync` | Edit title, description, rename or add choices; only if the version marker still matches; return Updated, Conflict or NotFound |
| `ResponseStore.AddAsync` | Record a response |
| `ResponseStore.ListAsync` | Responses to one poll, newest first |
| `ResponseStore.GetAsync` | One response, or null |
| `ResponseStore.UpdateChoiceAsync` | Change the chosen choice; same version check and outcomes |

A response stores the id of the choice, not its text, so renaming a choice doesn't break old responses. Choices can be renamed or added when editing a poll, never removed. There is no creator field yet.

## Code layout

| File | Role |
|---|---|
| `src/Program.cs` | Startup only: connects to Postgres, registers the stores, maps controllers |
| `src/Storage/PostgresBootstrap.cs` | Opens the connection pool, waits for Postgres, and runs the table-creation SQL |
| `src/Storage/PollStore.cs` | Poll reads and writes (done, the reference) |
| `src/Storage/ResponseStore.cs` | Response reads and writes |
| `src/Storage/UpdateOutcome.cs` | Updated, Conflict or NotFound, returned by the checked edits |
| `src/Models/` | `Poll` (with `Choice`), `PollResponse` and the two page view models |
| `src/Controllers/PollsController.cs` | Front page, create, the poll page, respond and edit poll |
| `src/Controllers/ResponsesController.cs` | The response page and edit response |
| `src/Controllers/HealthController.cs` | `/healthz` |
| `src/Views/` | Razor pages: shared layout, front page, poll page, response page |
| `src/wwwroot/` | `site.css`, `index.css`, `poll.css` and `copy-link.js` |

## Addresses

| Address | Page |
|---|---|
| `/` | List of polls and the create form |
| `/polls/{id}` | One poll: respond, see responses, edit (`?edit=true` shows the edit form) |
| `/polls/{pollId}/responses/{id}` | One response, editable |
| `/healthz` | Health check |
