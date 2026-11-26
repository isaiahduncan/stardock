using Npgsql;

/// <summary>
/// Reads and writes poll responses in Postgres. Setting up the tables is PostgresBootstrap's job.
/// The C# fills in the @parameters and reads result columns by position, so each statement has to use the parameter
/// names and column order described above it.
/// </summary>
public sealed class ResponseStore(NpgsqlDataSource db)
{
    // Parameters: @id (uuid), @pollId (uuid), @choiceId (uuid), @createdAt. The response's version should start at 1.
    private const string InsertResponseSql = """
        INSERT INTO responses (id, poll_id, choice_id, created_at)
        VALUES (@id, @pollId, @choiceId, @createdAt)
    """;

    // Parameter: @pollId (uuid). Columns, in this order:
    //   0 response id (uuid)   1 poll id (uuid)   2 choice id (uuid)   3 created_at (timestamptz)   4 version (integer)
    // Newest response first.
    private const string SelectResponsesSql = """
        SELECT r.id, r.poll_id, r.choice_id, r.created_at, r.version
        FROM responses r
        WHERE r.poll_id = @pollId
        ORDER BY r.created_at DESC
    """;

    // Parameters: @pollId (uuid), @id (uuid). Same columns as above, for one response.
    private const string SelectResponseSql = """
        SELECT r.id, r.poll_id, r.choice_id, r.created_at, r.version
        FROM responses r
        WHERE r.id = @id AND r.poll_id = @pollId
    """;

    // Parameters: @id (uuid), @pollId (uuid), @choiceId (uuid), @version (the version the editor saw).
    // Must only change the row if its version is still @version, and must bump the version when it does.
    // ExecuteNonQuery returns how many rows changed, so 0 means "nothing matched".
    private const string UpdateResponseSql = """
        UPDATE responses
        SET choice_id = @choiceId, version = version + 1
        WHERE id = @id AND poll_id = @pollId AND version = @version
    """;

    // Parameters: @id (uuid), @pollId (uuid). Returns a row if the response exists.
    private const string ResponseExistsSql = """
        SELECT 1
        FROM responses r
        WHERE r.id = @id AND r.poll_id = @pollId
    """;

    /// <summary>Records one response to a poll for the given choice.</summary>
    public async Task AddAsync(string pollId, string choiceId)
    {
        await using var command = db.CreateCommand(InsertResponseSql);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("pollId", Guid.Parse(pollId));
        command.Parameters.AddWithValue("choiceId", Guid.Parse(choiceId));
        command.Parameters.AddWithValue("createdAt", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Returns the responses to one poll, newest first.</summary>
    public async Task<IReadOnlyList<PollResponse>> ListAsync(string pollId)
    {
        // 1. Parse pollId with Guid.TryParse. If it is not valid there can be no responses: return an empty list.
        if(!Guid.TryParse(pollId, out var parsedPollId)) return new List<PollResponse>();
        // 2. Create a command with db.CreateCommand(SelectResponsesSql) and add the @pollId parameter.
        await using var command = db.CreateCommand(SelectResponsesSql);
        command.Parameters.AddWithValue("pollId", parsedPollId);

        // 3. Call ReadResponsesAsync(command) and return its result. The newest-first order comes from your SQL.
        return await ReadResponsesAsync(command);
    }

    /// <summary>Returns the response, or null if it does not exist (or an id is not a valid id).</summary>
    public async Task<PollResponse?> GetAsync(string pollId, string id)
    {
        // 1. Parse both ids with Guid.TryParse. If either is not valid, return null.
        if(!Guid.TryParse(pollId, out var parsedPollId)) return null;
        if(!Guid.TryParse(id, out var parsedResponseId)) return null;

        // 2. Create a command with db.CreateCommand(SelectResponseSql) and add the @pollId and @id parameters.
        await using var command = db.CreateCommand(SelectResponseSql);
        command.Parameters.AddWithValue("id", parsedResponseId);
        command.Parameters.AddWithValue("pollId", parsedPollId);

        // 3. Call ReadResponsesAsync(command) and return the first response, or null if there is none (FirstOrDefault).
        var responses = await ReadResponsesAsync(command);
        return responses.FirstOrDefault();
    }

    /// <summary>Changes which choice a response picked, only if it still has the version the editor saw.</summary>
    public async Task<UpdateOutcome> UpdateChoiceAsync(string pollId, string id, string seenETag, string choiceId)
    {
        // 1. Parse pollId and id with Guid.TryParse. If either is not valid, return UpdateOutcome.NotFound.
        // 2. Parse seenETag (the version number as text) with int.TryParse. If it is not a number, return UpdateOutcome.Conflict.
        if (!Guid.TryParse(pollId, out var parsedPollId)) return UpdateOutcome.NotFound;
        if (!Guid.TryParse(id, out var parsedResponseId)) return UpdateOutcome.NotFound;
        var parsedChoiceId = Guid.Parse(choiceId);

        if(!int.TryParse(seenETag, out int version)) return UpdateOutcome.Conflict;

        // 3. Create a command with db.CreateCommand(UpdateResponseSql) and add @id, @pollId, @choiceId
        //    (parse choiceId with Guid.Parse, the controller has already checked it belongs to the poll) and @version.
        //    This is one statement, so no transaction is needed.
        await using var command = db.CreateCommand(UpdateResponseSql);
        command.Parameters.AddWithValue("id", parsedResponseId);
        command.Parameters.AddWithValue("pollId", parsedPollId);
        command.Parameters.AddWithValue("choiceId", parsedChoiceId);
        command.Parameters.AddWithValue("version", version);
        
        // 4. Run it with ExecuteNonQueryAsync. If it changed at least 1 row, return UpdateOutcome.Updated.
        var rowsChanged = await command.ExecuteNonQueryAsync();

        // 5. 0 rows changed means nothing matched: either the response is gone or someone else edited it first.
        //    Run ResponseExistsSql with @id and @pollId (ExecuteScalarAsync returns null when there is no row).
        //    Return NotFound if it is gone, Conflict if it still exists.
        if(rowsChanged == 0)
        {
            await using var exists = db.CreateCommand(ResponseExistsSql);
            exists.Parameters.AddWithValue("id", parsedResponseId);
            exists.Parameters.AddWithValue("pollId", parsedPollId);
            return await exists.ExecuteScalarAsync() is null ? UpdateOutcome.NotFound : UpdateOutcome.Conflict;
        }

        return UpdateOutcome.Updated;
    }

    /// <summary>Turns the rows of a response query (columns as described above SelectResponsesSql) into responses.</summary>
    private static async Task<List<PollResponse>> ReadResponsesAsync(NpgsqlCommand command)
    {
        // 1. Make an empty List<PollResponse>.
        var responses = new List<PollResponse>();
        // 2. Run the command with command.ExecuteReaderAsync() ("await using var reader") and loop while await reader.ReadAsync()
        //    returns true. Each iteration is one row.
        await using var reader = await command.ExecuteReaderAsync();
        while(await reader.ReadAsync())
        {
            // 3. Build a PollResponse from the row: Id, PollId and ChoiceId from columns 0, 1 and 2 (reader.GetGuid(...).ToString()),
            //    CreatedAt from column 3 (reader.GetFieldValue<DateTimeOffset>(3)), and ETag from column 4 (reader.GetInt32(4) as text).
            //    Add it to the list.
            //SELECT r.id, r.poll_id, r.choice_id, r.created_at, r.version
            responses.Add(new PollResponse
            {
                Id = reader.GetGuid(0).ToString(),
                PollId = reader.GetGuid(1).ToString(),
                ChoiceId = reader.GetGuid(2).ToString(),
                CreatedAt = reader.GetFieldValue<DateTimeOffset>(3),
                ETag = reader.GetInt32(4).ToString(),
            });            
        }
        
        // 4. Return the list.
        return responses;
    }
}
