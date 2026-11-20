using Microsoft.Azure.Cosmos;

/// <summary>
/// One-time setup, run at startup: connects to Cosmos and makes sure the database and container exist.
/// It lives apart from CosmosStore so the store only reads and writes documents.
/// </summary>
public static class CosmosBootstrap
{
    private const string DatabaseName = "app";
    private const string ContainerName = "items";

    // CosmosStore writes a "partitionKey" field into every document; this is the path that field is read from.
    private const string PartitionKeyPath = "/partitionKey";

    /// <summary>
    /// Connects, creating the database and container if missing. Retries while the emulator container is still starting up.
    /// </summary>
    public static async Task<Container> CreateContainerAsync(string endpoint, string key)
    {
        var options = new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway, // the emulator only supports gateway mode
            LimitToEndpoint = true,
        };

        // The emulator's HTTPS certificate is self-signed. Trust anything, but only for a loopback endpoint.
        if (new Uri(endpoint).IsLoopback)
        {
            options.HttpClientFactory = () => new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            });
        }

        var client = new CosmosClient(endpoint, key, options);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var database = (await client.CreateDatabaseIfNotExistsAsync(DatabaseName)).Database;
                var container = await database.CreateContainerIfNotExistsAsync(
                    new ContainerProperties(ContainerName, PartitionKeyPath));
                return container.Container;
            }
            catch (Exception error) when (attempt < 30)
            {
                Console.WriteLine($"Waiting for the Cosmos emulator ({attempt}/30): {error.Message}");
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }
}
