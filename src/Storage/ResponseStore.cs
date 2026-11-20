using Microsoft.Azure.Cosmos;

/// <summary>Reads and writes poll responses. Setup of the container is CosmosBootstrap's job.</summary>
public sealed class ResponseStore(Container responses)
{
    /// <summary>Records one response to a poll for the given choice.</summary>
    public async Task AddAsync(string pollId, string choiceId)
    {
        var response = new PollResponse
        {
            Id = Guid.NewGuid().ToString(),
            PollId = pollId,
            ChoiceId = choiceId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await responses.CreateItemAsync(response, new PartitionKey(pollId));
    }

    /// <summary>Lists the responses to one poll, newest first. This reads a single partition.</summary>
    public async Task<IReadOnlyList<PollResponse>> ListAsync(string pollId)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.pollId = @pollId")
            .WithParameter("@pollId", pollId);
        var options = new QueryRequestOptions { PartitionKey = new PartitionKey(pollId) };

        var results = new List<PollResponse>();
        using var iterator = responses.GetItemQueryIterator<PollResponse>(query, requestOptions: options);
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync();
            results.AddRange(page);
        }

        // Sorted here instead of with ORDER BY, because the emulator supports only a subset of query features.
        return results.OrderByDescending(response => response.CreatedAt).ToList();
    }

    /// <summary>Returns the response, or null if it does not exist.</summary>
    public async Task<PollResponse?> GetAsync(string pollId, string id)
    {
        try
        {
            var result = await responses.ReadItemAsync<PollResponse>(id, new PartitionKey(pollId));
            result.Resource.ETag = result.ETag;
            return result.Resource;
        }
        catch (CosmosException error) when (error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Changes which choice a response picked.
    /// The save only goes through if the response still has the ETag the editor saw; otherwise nothing is saved.
    /// </summary>
    public async Task<UpdateOutcome> UpdateChoiceAsync(string pollId, string id, string seenETag, string choiceId)
    {
        var response = await GetAsync(pollId, id);
        if (response is null) return UpdateOutcome.NotFound;

        response.ChoiceId = choiceId;

        try
        {
            await responses.ReplaceItemAsync(
                response, id, new PartitionKey(pollId), new ItemRequestOptions { IfMatchEtag = seenETag });
            return UpdateOutcome.Updated;
        }
        catch (CosmosException error) when (error.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
        {
            return UpdateOutcome.Conflict;
        }
        catch (CosmosException error) when (error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return UpdateOutcome.NotFound;
        }
    }
}
