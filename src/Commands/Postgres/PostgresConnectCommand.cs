using System.ComponentModel;
using System.Text;
using Npgsql;
using Sliplane.Console.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Postgres;

/// <summary>
/// A small interactive SQL shell, so a database can be inspected without installing psql.
/// Statements run when a line ends with ';'. Backslash commands cover the common look-arounds.
/// </summary>
public sealed class PostgresConnectCommand : AsyncCommand<PostgresConnectCommand.Settings>
{
    public sealed class Settings : PostgresSettings
    {
        [CommandOption("--allow-my-ip")]
        [Description("If this machine's IP is blocked, add it to the database's allow list (it stays)")]
        public bool AllowMyIp { get; init; }
    }

    private const string Help = """
        Statements run when a line ends with ';'.
          \dt [schema]   list tables          \dn   list schemas
          \d <table>     describe a table     \l    list databases
          \x             toggle expanded rows \q    quit
        """;

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (!Interactive.IsTerminal)
            throw SliplaneException.Invalid("connect is interactive and needs a terminal.",
                "Use: sliplane postgres query --postgres <db> \"<sql>\"");

        var client = settings.CreateClient();
        var postgresId = await Resolve.PostgresAsync(client, settings.PostgresId);
        await using var source = await PostgresDb.OpenAsync(client, settings, postgresId, settings.AllowMyIp);
        await using var connection = await source.OpenConnectionAsync();

        AnsiConsole.MarkupLine($"[green]Connected[/] to [bold]{Markup.Escape(connection.Database)}[/] as {Markup.Escape(connection.UserName ?? "")} (PostgreSQL {Markup.Escape(connection.ServerVersion)}). [grey]\\? for help, \\q to quit.[/]");

        using var cts = new CancellationTokenSource();
        var expanded = false;
        var buffer = new StringBuilder();

        while (true)
        {
            System.Console.Write(buffer.Length == 0 ? $"{connection.Database}=> " : $"{connection.Database}-> ");
            var line = System.Console.ReadLine();
            if (line is null) break;

            var trimmed = line.Trim();
            if (buffer.Length == 0 && trimmed.StartsWith('\\'))
            {
                var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                var arg = parts.Length > 1 ? parts[1].Trim() : null;
                string? sql = parts[0] switch
                {
                    "\\q" => null,
                    "\\?" => "",
                    "\\x" => "",
                    "\\l" => "select datname as name, pg_size_pretty(pg_database_size(datname)) as size from pg_database where not datistemplate order by 1;",
                    "\\dn" => "select nspname as schema from pg_namespace where nspname not like 'pg\\_%' and nspname <> 'information_schema' order by 1;",
                    "\\dt" => "select table_schema as schema, table_name as name from information_schema.tables where table_type = 'BASE TABLE' and table_schema not in ('pg_catalog','information_schema')" +
                              (arg is null ? "" : $" and table_schema = {Quote(arg)}") + " order by 1, 2;",
                    "\\d" when arg is not null => "select column_name as column, data_type as type, is_nullable as nullable, column_default as default from information_schema.columns where " +
                              (arg.Contains('.') ? $"table_schema = {Quote(arg.Split('.')[0])} and table_name = {Quote(arg.Split('.')[1])}" : $"table_name = {Quote(arg)}") +
                              " order by table_schema, ordinal_position;",
                    _ => "?"
                };
                if (sql is null) break;
                if (parts[0] == "\\?") { System.Console.WriteLine(Help); continue; }
                if (parts[0] == "\\x") { expanded = !expanded; System.Console.WriteLine($"Expanded display is {(expanded ? "on" : "off")}."); continue; }
                if (sql == "?") { AnsiConsole.MarkupLine("[red]Unknown command.[/] \\? for help."); continue; }
                await Execute(connection, sql, expanded);
                continue;
            }

            buffer.AppendLine(line);
            if (!trimmed.EndsWith(';')) continue;

            var statement = buffer.ToString();
            buffer.Clear();
            await Execute(connection, statement, expanded);
        }
        return 0;
    }

    private static async Task Execute(NpgsqlConnection connection, string sql, bool expanded)
    {
        // Ctrl+C cancels the running statement, not the shell.
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 0 };
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; try { command.Cancel(); } catch { } };
        System.Console.CancelKeyPress += cancel;
        try
        {
            await using var reader = await command.ExecuteReaderAsync();
            do
            {
                if (reader.FieldCount == 0)
                {
                    AnsiConsole.MarkupLine($"[grey]{reader.RecordsAffected} row(s) affected[/]");
                    continue;
                }
                var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
                var rows = new List<string[]>();
                while (await reader.ReadAsync())
                    rows.Add(Enumerable.Range(0, reader.FieldCount)
                        .Select(i => reader.IsDBNull(i) ? "" : Convert.ToString(PostgresDb.Value(reader.GetValue(i))) ?? "").ToArray());
                Render(columns, rows, expanded);
            } while (await reader.NextResultAsync());
        }
        catch (PostgresException ex)
        {
            AnsiConsole.MarkupLine($"[red]ERROR:[/] {Markup.Escape(ex.MessageText)}" + (ex.Hint is null ? "" : $"\n[grey]HINT: {Markup.Escape(ex.Hint)}[/]"));
        }
        catch (NpgsqlException ex)
        {
            AnsiConsole.MarkupLine($"[red]ERROR:[/] {Markup.Escape(ex.Message)}");
        }
        finally
        {
            System.Console.CancelKeyPress -= cancel;
        }
    }

    private static void Render(List<string> columns, List<string[]> rows, bool expanded)
    {
        if (expanded)
        {
            for (var r = 0; r < rows.Count; r++)
            {
                AnsiConsole.MarkupLine($"[grey]-[[ RECORD {r + 1} ]]-[/]");
                var width = columns.Max(c => c.Length);
                for (var i = 0; i < columns.Count; i++)
                    System.Console.WriteLine($"{columns[i].PadRight(width)} | {rows[r][i]}");
            }
        }
        else
        {
            var table = new Table().Border(TableBorder.Simple);
            foreach (var c in columns) table.AddColumn(Markup.Escape(c));
            foreach (var row in rows) table.AddRow(row.Select(Markup.Escape).ToArray());
            AnsiConsole.Write(table);
        }
        AnsiConsole.MarkupLine($"[grey]({rows.Count} row{(rows.Count == 1 ? "" : "s")})[/]");
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
}
