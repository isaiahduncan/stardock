using Npgsql;

/// <summary>
/// Reads and writes polls in Postgres. Setting up the tables is PostgresBootstrap's job.
/// The C# fills in the @parameters and reads result columns by position, so each statement has to use the parameter
/// names and column order described above it.
/// </summary>
public sealed class PollStore(NpgsqlDataSource db)
{
    // Write. Parameters: @id (uuid), @title, @description, @createdAt.
    // The version column is left out on purpose: it defaults to 1 (see the schema), which is the ETag "1" that
    // CreateAsync sets on the poll it returns. Each @name below is filled in by Parameters.AddWithValue("name", ...).
    private const string InsertPollSql = """
        INSERT INTO polls (id, title, description, created_at)
        VALUES (@id, @title, @description, @createdAt)
        """;

    // Parameters: @id (uuid), @pollId (uuid), @text, @position (the choice's place in the list, starting at 0).
    private const string InsertChoiceSql = """
        INSERT INTO choices (id, poll_id, text, position)
        VALUES (@id, @pollId, @text, @position)
        """;

    // Read. Parameter: @id (the poll's id). Columns, in this order (the C# reads them by position):
    //   0 poll id (uuid)   1 title   2 description   3 created_at (timestamptz)   4 version (integer)
    //   5 choice id (uuid, null when the poll has no choices)   6 choice text
    // One row per choice. All the rows of one poll must be next to each other, with its choices in their original order.
    // LEFT JOIN keeps a poll that has no choices (it comes back as one row with nulls in columns 5 and 6, which is why the
    // C# checks IsDBNull(5)). ORDER BY c.position puts the choices back in the order they were created in.
    private const string SelectPollSql = """
        SELECT p.id, p.title, p.description, p.created_at, p.version, c.id, c.text
        FROM polls p
        LEFT JOIN choices c ON c.poll_id = p.id
        WHERE p.id = @id
        ORDER BY c.position
        """;

    // No parameters. Same columns and row rules as above, for every poll, newest poll first.
    // p.id is in the ORDER BY so that two polls created at the same moment still keep their rows together.
    private const string SelectPollsSql = """
        SELECT p.id, p.title, p.description, p.created_at, p.version, c.id, c.text
        FROM polls p
        LEFT JOIN choices c ON c.poll_id = p.id
        ORDER BY p.created_at DESC, p.id, c.position
        """;

    // Checked update (the Postgres version of the ETag). Parameters: @id (uuid), @version (the version the editor saw),
    // @title, @description. The WHERE clause is the check: it only matches while the row still has the version the editor
    // loaded. If someone saved first, their update already bumped the version, so this matches zero rows and
    // ExecuteNonQuery returns 0, which the C# treats as "not saved". Checking and changing happen in one statement,
    // so two editors can't both get through.
    private const string UpdatePollSql = """
        UPDATE polls
        SET title = @title, description = @description, version = version + 1
        WHERE id = @id AND version = @version
        """;

    // Parameter: @id (uuid). Returns a row if the poll exists. Used to tell "not found" from "changed by someone else".
    private const string PollExistsSql = """
        SELECT 1 FROM polls WHERE id = @id
        """;

    // Parameters: @id (the choice's uuid), @pollId (uuid), @text. Renames one choice of that poll.
    // Matching on poll_id too means an id from a different poll can't rename this poll's choice.
    private const string RenameChoiceSql = """
        UPDATE choices SET text = @text WHERE id = @id AND poll_id = @pollId
        """;

    // Parameters: @id (new choice uuid), @pollId (uuid), @text. Adds a choice after the poll's existing ones.
    // The position is one more than the highest existing one. MAX is null for a poll with no choices, so COALESCE
    // turns that into -1 and the first choice gets position 0.
    private const string AppendChoiceSql = """
        INSERT INTO choices (id, poll_id, text, position)
        SELECT @id, @pollId, @text, COALESCE(MAX(position), -1) + 1
        FROM choices
        WHERE poll_id = @pollId
        """;

    /// <summary>Saves a new poll and its choices in one transaction and returns it as saved.</summary>
    public async Task<Poll> CreateAsync(string title, string description, IEnumerable<string> choiceTexts)
    {
        var poll = new Poll
        {
            Id = Guid.NewGuid().ToString(),
            Title = title,
            Description = description,
            CreatedAt = DateTimeOffset.UtcNow,
            Choices = choiceTexts.Select(text => new Choice { Id = Guid.NewGuid().ToString(), Text = text }).ToList(),
            ETag = "1",
        };

        await using var connection = await db.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var command = new NpgsqlCommand(InsertPollSql, connection, transaction))
        {
            command.Parameters.AddWithValue("id", Guid.Parse(poll.Id));
            command.Parameters.AddWithValue("title", poll.Title);
            command.Parameters.AddWithValue("description", poll.Description);
            command.Parameters.AddWithValue("createdAt", poll.CreatedAt);
            await command.ExecuteNonQueryAsync();
        }

        for (var position = 0; position < poll.Choices.Count; position++)
        {
            await using var command = new NpgsqlCommand(InsertChoiceSql, connection, transaction);
            command.Parameters.AddWithValue("id", Guid.Parse(poll.Choices[position].Id));
            command.Parameters.AddWithValue("pollId", Guid.Parse(poll.Id));
            command.Parameters.AddWithValue("text", poll.Choices[position].Text);
            command.Parameters.AddWithValue("position", position);
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync(); // leaving without this rolls everything back
        return poll;
    }

    /// <summary>Returns the poll with its choices, or null if it does not exist (or the id is not a valid id).</summary>
    public async Task<Poll?> GetAsync(string id)
    {
        if (!Guid.TryParse(id, out var pollId)) return null; // the id comes from the address bar, so it may be nonsense

        await using var command = db.CreateCommand(SelectPollSql);
        command.Parameters.AddWithValue("id", pollId);

        var polls = await ReadPollsAsync(command);
        return polls.FirstOrDefault();
    }

    /// <summary>Returns every poll with its choices, newest first.</summary>
    public async Task<IReadOnlyList<Poll>> ListAsync()
    {
        await using var command = db.CreateCommand(SelectPollsSql);
        return await ReadPollsAsync(command);
    }

    /// <summary>
    /// Changes the title and description, renames existing choices (keyed by choice id) and appends new ones.
    /// Choices are never removed. The save only goes through if the poll still has the version the editor saw.
    /// </summary>
    public async Task<UpdateOutcome> UpdateAsync(
        string id,
        string seenETag,
        string title,
        string description,
        IReadOnlyDictionary<string, string> renamedChoices,
        IEnumerable<string> addedChoiceTexts)
    {
        if (!Guid.TryParse(id, out var pollId)) return UpdateOutcome.NotFound;
        if (!int.TryParse(seenETag, out var seenVersion)) return UpdateOutcome.Conflict; // not a number, so it can't match

        // Several rows change in this edit (the poll and its choices), so they all succeed or all fail together.
        await using var connection = await db.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        int rowsChanged;
        await using (var command = new NpgsqlCommand(UpdatePollSql, connection, transaction))
        {
            command.Parameters.AddWithValue("id", pollId);
            command.Parameters.AddWithValue("version", seenVersion);
            command.Parameters.AddWithValue("title", title);
            command.Parameters.AddWithValue("description", description);
            rowsChanged = await command.ExecuteNonQueryAsync();
        }

        if (rowsChanged == 0)
        {
            // Nothing matched: either the poll is gone or someone else edited it first. Leaving without committing saves nothing.
            await using var exists = new NpgsqlCommand(PollExistsSql, connection, transaction);
            exists.Parameters.AddWithValue("id", pollId);
            return await exists.ExecuteScalarAsync() is null ? UpdateOutcome.NotFound : UpdateOutcome.Conflict;
        }

        foreach (var (choiceId, text) in renamedChoices)
        {
            if (!Guid.TryParse(choiceId, out var choiceGuid)) continue;

            await using var command = new NpgsqlCommand(RenameChoiceSql, connection, transaction);
            command.Parameters.AddWithValue("id", choiceGuid);
            command.Parameters.AddWithValue("pollId", pollId);
            command.Parameters.AddWithValue("text", text);
            await command.ExecuteNonQueryAsync();
        }

        foreach (var text in addedChoiceTexts)
        {
            await using var command = new NpgsqlCommand(AppendChoiceSql, connection, transaction);
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("pollId", pollId);
            command.Parameters.AddWithValue("text", text);
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return UpdateOutcome.Updated;
    }

    /// <summary>Turns the rows of a poll query (columns as described above SelectPollSql) into polls.</summary>
    private static async Task<List<Poll>> ReadPollsAsync(NpgsqlCommand command)
    {
        var polls = new List<Poll>();

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetGuid(0).ToString();

            // Rows of the same poll are next to each other, so a new id means a new poll.
            if (polls.Count == 0 || polls[^1].Id != id)
            {
                polls.Add(new Poll
                {
                    Id = id,
                    Title = reader.GetString(1),
                    Description = reader.GetString(2),
                    CreatedAt = reader.GetFieldValue<DateTimeOffset>(3),
                    ETag = reader.GetInt32(4).ToString(),
                });
            }

            // Columns 5 and 6 are this row's choice; they are null when the poll has no choices.
            if (!reader.IsDBNull(5))
            {
                polls[^1].Choices.Add(new Choice { Id = reader.GetGuid(5).ToString(), Text = reader.GetString(6) });
            }
        }

        return polls;
    }
}
