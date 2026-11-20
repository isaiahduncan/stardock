# Interview 3 starter: C# + Cosmos DB emulator

A polling site on C# and a Cosmos DB emulator. People create a poll (a title, a description and a list of choices), anyone can open a poll and submit one response by picking a choice, and polls and responses can be edited afterwards. Edits are checked against the document's ETag, so an edit made from a stale page is rejected, not silently overwritten. Two containers: `polls` (partitioned by `/id`) and `responses` (partitioned by `/pollId`).

**Status: not compiled or run.** It was written without access to the .NET SDK or the emulator. Do a full run once on your machine before interview day (see "First run" below) and fix anything that surfaces.

## First run

```bash
docker compose up -d                      # starts the Cosmos emulator over HTTPS
cd src
dotnet run --urls http://localhost:5000   # waits for the emulator, then serves the app
```

- App: http://localhost:5000
- Data Explorer (browse the data): port 1234 on localhost. Try http first, then https.
- The app retries for about a minute while the emulator boots, so starting both back to back is fine.

## Why HTTPS

Microsoft's docs say the .NET SDK doesn't support the emulator's default HTTP mode, so `compose.yaml` starts it with `--protocol https`. The emulator's certificate is self-signed. `Storage/CosmosBootstrap.cs` accepts any certificate, but only when the endpoint is loopback (localhost).

## Settings

| Setting | Default |
|---|---|
| `COSMOS_ENDPOINT` | `https://localhost:8081` |
| `COSMOS_KEY` | the publicly documented emulator key (the emulator doesn't check it) |

## Compose file name

Docker Compose finds the file on its own if it is named `compose.yaml` (preferred), `compose.yml`, `docker-compose.yaml` or `docker-compose.yml`. Anything else, like `compose.postgres.yaml` here, needs `-f`:

```bash
docker compose -f compose.postgres.yaml up -d
```

## Useful commands

| Command | What it does |
|---|---|
| `docker compose up -d` | Start the emulator |
| `docker compose down` | Stop it, keep the data |
| `docker compose down -v` | Stop it and wipe the data |
| `docker compose logs -f cosmos` | Watch emulator logs |

## Data

| Container | Partition key | Fields |
|---|---|---|
| `polls` | `/id` | `id`, `title`, `description`, `choices` (each with `id` and `text`), `createdAt` |
| `responses` | `/pollId` | `id`, `pollId`, `choiceId`, `createdAt` |

A response stores the id of the choice, not its text, so renaming a choice doesn't break old responses. Choices can be renamed or added when editing a poll, never removed. There is no creator field yet; add one when there are accounts.

## Known emulator limits (from Microsoft's docs)

- NoSQL API only, gateway mode only, and only a subset of Cosmos features.
- The .NET SDK doesn't support bulk execution against the emulator.
- `vnext-latest` is a rolling tag. Pin a specific tag once you have a version that works for you.

## Fallback: Postgres

`compose.postgres.yaml` starts an empty Postgres 16. Use it if the spec is clearly relational. You would add Npgsql to the project; the C# code here only talks to Cosmos.

## Code layout

| File | Role |
|---|---|
| `src/Program.cs` | Startup only: runs the Cosmos bootstrap, registers the stores, maps controllers |
| `src/Storage/CosmosBootstrap.cs` | Startup setup: connects, creates the database and both containers, retries while the emulator boots |
| `src/Storage/PollStore.cs` | Reads and writes poll documents (create, get, list, ETag-checked edit) |
| `src/Storage/ResponseStore.cs` | Reads and writes responses (add, list for a poll, get, ETag-checked change of choice) |
| `src/Storage/UpdateOutcome.cs` | Updated, Conflict or NotFound, returned by the checked edits |
| `src/Models/` | `Poll` (with `Choice`), `PollResponse` and the two page view models |
| `src/Controllers/PollsController.cs` | Front page, create, the poll page, respond and edit poll |
| `src/Controllers/ResponsesController.cs` | The response page and edit response |
| `src/Controllers/HealthController.cs` | `/healthz` |
| `src/Views/Shared/_Layout.cshtml` | The page shell every view sits inside (head, Pico, `site.css`) |
| `src/Views/Polls/Index.cshtml` | Front page: create form on the left, list of polls on the right |
| `src/Views/Polls/Details.cshtml` | Poll page: poll info, edit and respond on the left, responses on the right |
| `src/Views/Responses/Details.cshtml` | Response page: change which choice a response picked |
| `src/wwwroot/copy-link.js` | The poll page's Copy link button |
| `src/wwwroot/site.css`, `index.css`, `poll.css` | Shared styles (including the two-column grid), front-page styles, poll and response page styles |

## Addresses

| Address | Page |
|---|---|
| `/` | List of polls and the create form |
| `/polls/{id}` | One poll: respond, see responses, edit (`?edit=true` shows the edit form) |
| `/polls/{pollId}/responses/{id}` | One response, editable |
| `/healthz` | Health check |
