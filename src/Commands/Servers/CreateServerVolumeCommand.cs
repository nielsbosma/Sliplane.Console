using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Servers;

public sealed class CreateServerVolumeCommand : AsyncCommand<CreateServerVolumeCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--server-id|--server <SERVER>")]
        [Description("Server name or ID")]
        public required string ServerId { get; init; }

        [CommandOption("--name <NAME>")]
        [Description("The name of the volume")]
        public required string Name { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var serverId = await Resolve.ServerAsync(client, settings.ServerId);
        var result = await client.PostAsync($"servers/{serverId}/volumes", new { name = settings.Name });
        Output.Write(result);
        return 0;
    }
}
