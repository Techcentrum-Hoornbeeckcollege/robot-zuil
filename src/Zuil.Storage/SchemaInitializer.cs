using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Zuil.Storage;

/// <summary>
/// Applies the embedded .sql migrations at startup. Every script is written to
/// be idempotent (CREATE TABLE IF NOT EXISTS), so re-running on an existing
/// cache is a no-op.
/// </summary>
public static class SchemaInitializer
{
    public static void Apply(SqliteConnection connection, ILogger logger)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var names = assembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        foreach (var name in names)
        {
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Missing embedded migration {name}");
            using var reader = new StreamReader(stream);
            var sql = reader.ReadToEnd();

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();

            logger.LogInformation("Applied migration {Migration}", name);
        }
    }
}
