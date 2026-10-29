using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Postgres;

public sealed class RestorePostgresCommand : AsyncCommand<RestorePostgresCommand.Settings>
{
    public sealed class Settings : PostgresSettings
    {
        [CommandOption("--timestamp <TIMESTAMP>")]
        [Description("The point in time to restore to (RFC 3339), within the restore window")]
        public required string Timestamp { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var postgresId = await Resolve.PostgresAsync(client, settings.PostgresId);
        var result = await client.PostAsync($"postgres/{postgresId}/restore", new { timestamp = settings.Timestamp });
        Output.Write(result);
        return 0;
    }
}
