# Interview 3 starter: C# + Cosmos DB emulator

No domain data model. Documents are plain JSON, stored under a partition key value that you type into the form. The page, the form and the JSON API work for any spec until you decide on real types.

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

## When the spec arrives

1. Ask the PM your questions and write down the access patterns (what gets read, how often, by which key).
2. Choose the partition key from those patterns. `/partitionKey` in `Storage/CosmosBootstrap.cs` is a placeholder. Be ready to explain the choice and the tradeoffs.
3. Replace the generic JSON form in `Views/Home/Index.cshtml` with real fields, and add typed models once the shape is clear.

## Known emulator limits (from Microsoft's docs)

- NoSQL API only, gateway mode only, and only a subset of Cosmos features.
- The .NET SDK doesn't support bulk execution against the emulator.
- `vnext-latest` is a rolling tag. Pin a specific tag once you have a version that works for you.

## Fallback: Postgres

`compose.postgres.yaml` starts an empty Postgres 16. Use it if the spec is clearly relational. You would add Npgsql to the project; the C# code here only talks to Cosmos.

## Code layout

| File | Role |
|---|---|
| `src/Program.cs` | Startup only: connects to Cosmos, registers services, maps controllers |
| `src/Controllers/ItemsController.cs` | JSON API under `/api/items` |
| `src/Controllers/HomeController.cs` | The HTML page, form handlers and `/healthz` |
| `src/Storage/CosmosBootstrap.cs` | Startup setup: connects, creates the database and container, retries while the emulator boots |
| `src/Storage/CosmosStore.cs` | Reads and writes documents (add, list, delete); no setup code |
| `src/Views/Home/Index.cshtml` | The page, as a Razor template (HTML-encodes output automatically) |
