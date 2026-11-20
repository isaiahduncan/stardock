using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

/// <summary>
/// Schema-less access to one Cosmos container. Documents are plain JSON objects, so nothing here
/// depends on the interview's data model. Every document gets a GUID "id" and a "partitionKey" field,
/// which is also the partition key. Creating the database and container is CosmosBootstrap's job, not this class's.
/// </summary>
public sealed class CosmosStore(Container items)
{
    /// <summary>Stores a document under the given partition key value and returns it as saved.</summary>
    public async Task<JObject> AddAsync(string partitionKey, JObject document)
    {
        document["id"] = Guid.NewGuid().ToString();
        document["partitionKey"] = partitionKey;
        var response = await items.CreateItemAsync(document, new PartitionKey(partitionKey));
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
        using var iterator = items.GetItemQueryIterator<JObject>(query, requestOptions: requestOptions);
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
            await items.DeleteItemAsync<JObject>(id, new PartitionKey(partitionKey));
            return true;
        }
        catch (CosmosException error) when (error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
