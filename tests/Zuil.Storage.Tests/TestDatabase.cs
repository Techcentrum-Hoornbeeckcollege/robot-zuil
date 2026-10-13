using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Zuil.Storage.Tests;

/// <summary>
/// A real SQLite file in a throwaway directory, with the migrations applied.
///
/// A file rather than :memory: on purpose: one of the things worth testing is
/// that the Graph secret never appears in the database's bytes, and that needs
/// bytes on a disk to look at. It also means WAL mode behaves as it does in
/// production.
/// </summary>
internal sealed class TestDatabase : IDisposable
{
    private readonly string _directory;

    public TestDatabase()
    {
        _directory = Path.Combine(Path.GetTempPath(), "zuil-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_directory);

        Path_ = Path.Combine(_directory, "zuil-cache.db");

        Database = new ZuilDatabase(
            Options.Create(new StorageOptions { DatabasePath = Path_ }),
            NullLogger<ZuilDatabase>.Instance);

        Protector = new AesGcmSecretProtector(
            Database.SecretKeyPath,
            NullLogger<AesGcmSecretProtector>.Instance);

        Settings = new SqliteSettingsStore(
            Database,
            Protector,
            Options.Create(new GraphCredentialOptions()),
            NullLogger<SqliteSettingsStore>.Instance);

        Meetings = new SqliteMeetingStore(Database, NullLogger<SqliteMeetingStore>.Instance);
    }

    public string Path_ { get; }

    public ZuilDatabase Database { get; }

    public AesGcmSecretProtector Protector { get; }

    public SqliteSettingsStore Settings { get; }

    public SqliteMeetingStore Meetings { get; }

    /// <summary>Every byte of the database file, including the write-ahead log.</summary>
    public byte[] RawBytes()
    {
        using var checkpoint = Database.Connection.CreateCommand();
        // Without this the newest rows are still in the -wal file, and a test
        // asserting what is or is not in the database would be reading a
        // snapshot that predates the thing it is checking.
        checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        checkpoint.ExecuteNonQuery();

        return File.ReadAllBytes(Path_);
    }

    public void Dispose()
    {
        Database.Dispose();

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }
}
