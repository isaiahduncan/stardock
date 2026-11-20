// The emulator ignores authentication, but the SDK still wants a valid-looking key.
// This is the publicly documented emulator key, not a secret.
const string WellKnownEmulatorKey =
    "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

var builder = WebApplication.CreateBuilder(args);

var endpoint = builder.Configuration["COSMOS_ENDPOINT"] ?? "https://localhost:8081";
var key = builder.Configuration["COSMOS_KEY"] ?? WellKnownEmulatorKey;

var containers = await CosmosBootstrap.CreateContainersAsync(endpoint, key);
builder.Services.AddSingleton(new PollStore(containers.Polls));
builder.Services.AddSingleton(new ResponseStore(containers.Responses));
builder.Services.AddControllersWithViews();

var app = builder.Build();

app.UseStaticFiles();
app.MapControllers();

app.Run();
