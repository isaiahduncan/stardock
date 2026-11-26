var builder = WebApplication.CreateBuilder(args);

// Matches compose.postgres.yaml. Override with the POSTGRES_CONNECTION setting.
var connectionString = builder.Configuration["POSTGRES_CONNECTION"]
    ?? "Host=localhost;Port=5432;Username=app;Password=app;Database=app";

var dataSource = await PostgresBootstrap.CreateDataSourceAsync(connectionString);
builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton<PollStore>();
builder.Services.AddSingleton<ResponseStore>();
builder.Services.AddControllersWithViews();

var app = builder.Build();

app.UseStaticFiles();
app.MapControllers();

app.Run();
