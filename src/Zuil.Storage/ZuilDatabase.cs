using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Zuil.Storage;

/// <summary>
/// The one SQLite connection this process uses, and the one lock that serializes
/// writes to it.
///
/// Extracted from <see cref="SqliteMeetingStore"/> when the settings store
/// arrived, because the alternative — a second connection — reintroduces exactly
/// the SQLITE_BUSY contention the single-connection-plus-semaphore arrangement
/// exists to avoid. Both stores share this; neither owns it.
/// </summary>
public sealed class ZuilDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    // SQLite would serialize writes anyway; doing it here turns lock contention
    // into an await instead of a SQLITE_BUSY exception.
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <param name="dataDirectory">
    /// Where the database and its key file live. Chosen by
    /// <see cref="DataDirectory.Resolve"/> rather than configured, because the
    /// configuration is inside the database.
    /// </param>
    public ZuilDatabase(string dataDirectory, ILogger<ZuilDatabase> logger)
    {
        Directory.CreateDirectory(dataDirectory);
        DatabasePath = Path.Combine(Path.GetFullPath(dataDirectory), "zuil-cache.db");

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        };

        _connection = new SqliteConnection(builder.ToString());
        _connection.Open();

        using (var pragma = _connection.CreateCommand())
        {
            // WAL so a long sync write never blocks a visitor's search.
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();
        }

        SchemaInitializer.Apply(_connection, logger);
    }

    /// <summary>Absolute path to the database file.</summary>
    public string DatabasePath { get; }

    /// <summary>
    /// Where the secret key lives: beside the database, so backing up one
    /// without the other is at least an obvious mistake.
    /// </summary>
    public string SecretKeyPath =>
        Path.Combine(Path.GetDirectoryName(DatabasePath) ?? ".", "secret.key");

    /// <summary>
    /// The shared connection. Readers use it directly; writers take
    /// <see cref="AcquireWriteAsync"/> first.
    /// </summary>
    public SqliteConnection Connection => _connection;

    /// <summary>
    /// Takes the write lock. Dispose the returned lease to release it:
    /// <c>using var lease = await db.AcquireWriteAsync(ct);</c>
    /// </summary>
    public async Task<WriteLease> AcquireWriteAsync(CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        return new WriteLease(_writeLock);
    }

    /// <summary>Releases the write lock when disposed.</summary>
    public readonly struct WriteLease : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;

        internal WriteLease(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose() => _semaphore.Release();
    }

    public void Dispose()
    {
        _writeLock.Dispose();
        _connection.Dispose();
    }
}
