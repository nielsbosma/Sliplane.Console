using Sliplane.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Postgres;

public sealed class RotatePostgresCredentialsCommand : AsyncCommand<PostgresSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PostgresSettings settings)
    {
        var client = settings.CreateClient();
        var postgresId = await Resolve.PostgresAsync(client, settings.PostgresId);
        var result = await client.PostAsync($"postgres/{postgresId}/credentials/rotate");
        Output.Write(result);
        return 0;
    }
}
