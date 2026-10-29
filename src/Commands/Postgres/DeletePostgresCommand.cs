using Sliplane.Console.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Postgres;

public sealed class DeletePostgresCommand : AsyncCommand<PostgresSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PostgresSettings settings)
    {
        var client = settings.CreateClient();
        var postgresId = await Resolve.PostgresAsync(client, settings.PostgresId);
        settings.Confirm($"Permanently delete Postgres database {postgresId} and its data?");
        await client.DeleteAsync($"postgres/{postgresId}");
        AnsiConsole.MarkupLine("[green]Postgres database deleted.[/]");
        return 0;
    }
}
