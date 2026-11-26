using Npgsql;

/// <summary>
/// One-time setup, run at startup: connects to Postgres (retrying while the container is still starting up)
/// and creates the tables. It lives apart from the stores so they only read and write data.
/// </summary>
public static class PostgresBootstrap
{
    // The tables for polls, choices and responses.
    // This runs on every startup, so every statement uses IF NOT EXISTS.
    // polls.version goes up by one on every edit and is the ETag. choices.poll_id points at its poll, and
    // ON DELETE CASCADE removes a poll's choices if the poll is ever deleted. choices.position is the choice's place
    // in the list, starting at 0. (No comments inside the SQL itself, to keep the statement parsing simple.)
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS polls (
            id           uuid         PRIMARY KEY,
            title        text         NOT NULL,
            description  text         NOT NULL,
            created_at   timestamptz  NOT NULL,
            version      integer      NOT NULL DEFAULT 1
        );

        CREATE TABLE IF NOT EXISTS choices (
            id        uuid     PRIMARY KEY,
            poll_id   uuid     NOT NULL REFERENCES polls (id) ON DELETE CASCADE,
            text      text     NOT NULL,
            position  integer  NOT NULL
        );

        CREATE TABLE IF NOT EXISTS responses (
            id          uuid            PRIMARY KEY,
            poll_id     uuid            NOT NULL REFERENCES polls (id) ON DELETE CASCADE,
            choice_id      uuid            NOT NULL REFERENCES choices (id),
            created_at  timestamptz     NOT NULL,
            version     integer         NOT NULL DEFAULT 1
        );

        CREATE INDEX IF NOT EXISTS choices_poll_id_idx ON choices (poll_id);
        CREATE INDEX IF NOT EXISTS responses_poll_id_idx ON responses (poll_id);
        """;

    /// <summary>Opens the shared connection pool, waits until Postgres answers, and creates the tables.</summary>
    public static async Task<NpgsqlDataSource> CreateDataSourceAsync(string connectionString)
    {
        var dataSource = NpgsqlDataSource.Create(connectionString);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var command = dataSource.CreateCommand(SchemaSql);
                await command.ExecuteNonQueryAsync();
                return dataSource;
            }
            catch (Exception error) when (attempt < 30 && CanBeRetried(error))
            {
                Console.WriteLine($"Waiting for Postgres ({attempt}/30): {error.Message}");
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }

    /// <summary>
    /// A refused connection, or Postgres saying it is still starting up, is worth retrying.
    /// Any other error from the database (like a typo in your SQL) is not, so it shows up right away.
    /// </summary>
    private static bool CanBeRetried(Exception error) =>
        error is not PostgresException postgresError || postgresError.SqlState == PostgresErrorCodes.CannotConnectNow;
}
