using Microsoft.Azure.Cosmos;

/// <summary>The two containers the app uses.</summary>
public sealed record CosmosContainers(Container Polls, Container Responses);

/// <summary>
/// One-time setup, run at startup: connects to Cosmos and makes sure the database and both containers exist.
/// It lives apart from the stores so they only read and write documents.
/// </summary>
public static class CosmosBootstrap
{
    private const string DatabaseName = "app";

    // Each poll is its own partition, so reading one poll by id is a single-partition point read.
    private const string PollsContainerName = "polls";
    private const string PollsPartitionKeyPath = "/id";

    // Responses are partitioned by poll, so all the responses to one poll live together and can be counted in one partition.
    private const string ResponsesContainerName = "responses";
    private const string ResponsesPartitionKeyPath = "/pollId";

    /// <summary>
    /// Connects, creating the database and containers if missing. Retries while the emulator container is still starting up.
    /// </summary>
    public static async Task<CosmosContainers> CreateContainersAsync(string endpoint, string key)
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
                var polls = await database.CreateContainerIfNotExistsAsync(
                    new ContainerProperties(PollsContainerName, PollsPartitionKeyPath));
                var responses = await database.CreateContainerIfNotExistsAsync(
                    new ContainerProperties(ResponsesContainerName, ResponsesPartitionKeyPath));
                return new CosmosContainers(polls.Container, responses.Container);
            }
            catch (Exception error) when (attempt < 30)
            {
                Console.WriteLine($"Waiting for the Cosmos emulator ({attempt}/30): {error.Message}");
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }
}
