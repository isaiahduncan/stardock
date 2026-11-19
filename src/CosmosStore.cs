using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

/// <summary>
/// Schema-less access to one Cosmos container. Documents are plain JSON objects, so nothing here
/// depends on the interview's data model. Every document gets a GUID "id" and a "partitionKey" field,
/// which is also the partition key.
/// </summary>
public sealed class CosmosStore
{
    private const string DatabaseName = "app";
    private const string ContainerName = "items";
    private const string PartitionKeyPath = "/partitionKey";

    private readonly Container _items;

    private CosmosStore(Container items) => _items = items;

    /// <summary>Connects, retrying while the emulator container is still starting up.</summary>
    public static async Task<CosmosStore> ConnectAsync(string endpoint, string key)
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
                var container = (await database.CreateContainerIfNotExistsAsync(
                    new ContainerProperties(ContainerName, PartitionKeyPath))).Container;
                return new CosmosStore(container);
            }
            catch (Exception error) when (attempt < 30)
            {
                Console.WriteLine($"Waiting for the Cosmos emulator ({attempt}/30): {error.Message}");
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }

    /// <summary>Stores a document under the given partition key value and returns it as saved.</summary>
    public async Task<JObject> AddAsync(string partitionKey, JObject document)
    {
        document["id"] = Guid.NewGuid().ToString();
        document["partitionKey"] = partitionKey;
        var response = await _items.CreateItemAsync(document, new PartitionKey(partitionKey));
        return response.Resource;
    }

    /// <summary>Lists documents, newest first. Pass a partition key value to read a single partition.</summary>
    public async Task<List<JObject>> ListAsync(string? partitionKey)
    {
        QueryDefinition query;
        QueryRequestOptions? requestOptions = null;

        if (string.IsNullOrWhiteSpace(partitionKey))
        {
            query = new QueryDefinition("SELECT * FROM c");
        }
        else
        {
            query = new QueryDefinition("SELECT * FROM c WHERE c.partitionKey = @partitionKey")
                .WithParameter("@partitionKey", partitionKey);
            requestOptions = new QueryRequestOptions { PartitionKey = new PartitionKey(partitionKey) };
        }

        var results = new List<JObject>();
        using var iterator = _items.GetItemQueryIterator<JObject>(query, requestOptions: requestOptions);
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync();
            results.AddRange(page);
        }

        // Sorted here instead of with ORDER BY, because the emulator supports only a subset of query features.
        return results.OrderByDescending(document => (long?)document["_ts"] ?? 0).ToList();
    }

    /// <summary>Deletes one document. Returns false if it does not exist.</summary>
    public async Task<bool> DeleteAsync(string partitionKey, string id)
    {
        try
        {
            await _items.DeleteItemAsync<JObject>(id, new PartitionKey(partitionKey));
            return true;
        }
        catch (CosmosException error) when (error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
