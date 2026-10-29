using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Servers;

public sealed class DeleteServerCommand : AsyncCommand<DeleteServerCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--server-id|--server <SERVER>")]
        [Description("Server name or ID to delete")]
        public required string ServerId { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var serverId = await Resolve.ServerAsync(client, settings.ServerId);
        settings.Confirm($"Delete server {serverId} and everything on it?");
        await client.DeleteAsync($"servers/{serverId}");
        AnsiConsole.MarkupLine("[green]Server deleted.[/]");
        return 0;
    }
}
