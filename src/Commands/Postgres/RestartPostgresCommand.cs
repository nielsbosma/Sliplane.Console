using Sliplane.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Postgres;

public sealed class RestartPostgresCommand : AsyncCommand<PostgresSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PostgresSettings settings)
    {
        var client = settings.CreateClient();
        var postgresId = await Resolve.PostgresAsync(client, settings.PostgresId);
        var result = await client.PostAsync($"postgres/{postgresId}/restart");
        Output.Write(result);
        return 0;
    }
}
