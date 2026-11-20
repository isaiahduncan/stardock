using Microsoft.Azure.Cosmos;

/// <summary>Reads and writes poll documents. Setup of the container is CosmosBootstrap's job.</summary>
public sealed class PollStore(Container polls)
{
    /// <summary>Saves a new poll with a fresh id and one choice per text, and returns it as saved.</summary>
    public async Task<Poll> CreateAsync(string title, string description, IEnumerable<string> choiceTexts)
    {
        var poll = new Poll
        {
            Id = Guid.NewGuid().ToString(),
            Title = title,
            Description = description,
            Choices = choiceTexts.Select(NewChoice).ToList(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await polls.CreateItemAsync(poll, new PartitionKey(poll.Id));
        return poll;
    }

    /// <summary>Returns the poll, or null if it does not exist.</summary>
    public async Task<Poll?> GetAsync(string id)
    {
        try
        {
            var response = await polls.ReadItemAsync<Poll>(id, new PartitionKey(id));
            response.Resource.ETag = response.ETag;
            return response.Resource;
        }
        catch (CosmosException error) when (error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>Lists all polls, newest first. This reads across partitions, which is fine at this scale.</summary>
    public async Task<IReadOnlyList<Poll>> ListAsync()
    {
        var results = new List<Poll>();
        using var iterator = polls.GetItemQueryIterator<Poll>("SELECT * FROM c");
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync();
            results.AddRange(page);
        }

        // Sorted here instead of with ORDER BY, because the emulator supports only a subset of query features.
        return results.OrderByDescending(poll => poll.CreatedAt).ToList();
    }

    /// <summary>
    /// Changes the title and description, renames existing choices (keyed by choice id) and appends new ones.
    /// Choices are never removed, so stored responses always point at a real choice.
    /// The save only goes through if the poll still has the ETag the editor saw; otherwise nothing is saved.
    /// </summary>
    public async Task<UpdateOutcome> UpdateAsync(
        string id,
        string seenETag,
        string title,
        string description,
        IReadOnlyDictionary<string, string> renamedChoices,
        IEnumerable<string> addedChoiceTexts)
    {
        var poll = await GetAsync(id);
        if (poll is null) return UpdateOutcome.NotFound;

        poll.Title = title;
        poll.Description = description;

        foreach (var choice in poll.Choices)
        {
            if (renamedChoices.TryGetValue(choice.Id, out var text)) choice.Text = text;
        }

        poll.Choices.AddRange(addedChoiceTexts.Select(NewChoice));

        try
        {
            await polls.ReplaceItemAsync(
                poll, id, new PartitionKey(id), new ItemRequestOptions { IfMatchEtag = seenETag });
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

    private static Choice NewChoice(string text) => new() { Id = Guid.NewGuid().ToString(), Text = text };
}
