using EngineeringMcp.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EngineeringMcp.Sql;

/// <summary>
/// Creates the demo SQLite database from <see cref="DatabaseOptions.SeedScript"/> the first time
/// the server starts. This is the only code path that opens the database for writing, and it
/// only runs when the file does not exist yet.
/// </summary>
public sealed partial class SqliteDemoSeeder(IOptions<DatabaseOptions> options, ILogger<SqliteDemoSeeder> logger)
{
    /// <returns><see langword="true"/> when a new database file was created.</returns>
    public bool EnsureCreated()
    {
        var settings = options.Value;
        if (settings.Provider != SqlDialect.Sqlite || string.IsNullOrWhiteSpace(settings.SeedScript))
        {
            return false;
        }

        var builder = new SqliteConnectionStringBuilder(settings.ConnectionString);
        var databasePath = Path.GetFullPath(builder.DataSource);
        if (File.Exists(databasePath))
        {
            return false;
        }

        var scriptPath = Path.GetFullPath(settings.SeedScript);
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("The configured seed script does not exist.", scriptPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        builder.DataSource = databasePath;
        builder.Mode = SqliteOpenMode.ReadWriteCreate;
        builder.Pooling = false;

        using (var connection = new SqliteConnection(builder.ToString()))
        {
            connection.Open();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = File.ReadAllText(scriptPath);
            command.ExecuteNonQuery();
            transaction.Commit();
        }

        LogSeeded(databasePath, scriptPath);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created demo database {DatabasePath} from {ScriptPath}")]
    private partial void LogSeeded(string databasePath, string scriptPath);
}
