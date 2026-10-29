using System.ComponentModel;
using Npgsql;
using Sliplane.Console.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Postgres;

/// <summary>
/// Runs SQL as the database owner and prints the rows. No psql, no connection string: the
/// credentials come from the API and never reach the command line or the output.
/// </summary>
public sealed class PostgresQueryCommand : AsyncCommand<PostgresQueryCommand.Settings>
{
    public sealed class Settings : PostgresSettings
    {
        [CommandArgument(0, "[SQL]")]
        [Description("The SQL to run. Omit to read it from --file or stdin")]
        public string? Sql { get; init; }

        [CommandOption("-f|--file <FILE>")]
        [Description("Read SQL from a file ('-' for stdin)")]
        public string? File { get; init; }

        [CommandOption("--timeout <SECONDS>")]
        [Description("Command timeout in seconds (default 60, 0 = none)")]
        public int Timeout { get; init; } = 60;

        [CommandOption("--allow-my-ip")]
        [Description("If this machine's IP is blocked, add it to the database's allow list (it stays)")]
        public bool AllowMyIp { get; init; }

        public override ValidationResult Validate()
        {
            if (Sql is not null && File is not null) return ValidationResult.Error("Pass SQL as an argument or --file, not both.");
            if (Timeout < 0) return ValidationResult.Error("--timeout cannot be negative.");
            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var sql = settings.Sql
                  ?? (settings.File is null or "-"
                      ? (System.Console.IsInputRedirected ? await System.Console.In.ReadToEndAsync() : null)
                      : await System.IO.File.ReadAllTextAsync(settings.File));
        if (string.IsNullOrWhiteSpace(sql))
            throw SliplaneException.Invalid("No SQL given.", "Pass it as an argument, with --file, or on stdin.");

        var client = settings.CreateClient();
        var postgresId = await Resolve.PostgresAsync(client, settings.PostgresId);
        await using var source = await PostgresDb.OpenAsync(client, settings, postgresId, settings.AllowMyIp);
        await using var connection = await source.OpenConnectionAsync();

        var results = await RunAsync(connection, sql, settings.Timeout);
        Output.WriteObject(results.Count == 1 ? results[0] : results);
        return 0;
    }

    /// <summary>One entry per statement: its rows when it returned any, else the affected count.</summary>
    internal static async Task<List<object>> RunAsync(NpgsqlConnection connection, string sql, int timeout)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = timeout };
        await using var reader = await command.ExecuteReaderAsync();

        var results = new List<object>();
        do
        {
            if (reader.FieldCount == 0)
            {
                results.Add(new Dictionary<string, object?> { ["rowsAffected"] = reader.RecordsAffected });
                continue;
            }
            var rows = new List<Dictionary<string, object?>>();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (var i = 0; i < reader.FieldCount; i++)
                    row[Unique(row, reader.GetName(i))] = PostgresDb.Value(reader.IsDBNull(i) ? null : reader.GetValue(i));
                rows.Add(row);
            }
            results.Add(rows);
        } while (await reader.NextResultAsync());

        return results;
    }

    private static string Unique(Dictionary<string, object?> row, string name)
    {
        if (!row.ContainsKey(name)) return name;
        var n = 2;
        while (row.ContainsKey($"{name}_{n}")) n++;
        return $"{name}_{n}";
    }
}
