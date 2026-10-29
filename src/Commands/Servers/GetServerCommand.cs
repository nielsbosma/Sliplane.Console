using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Servers;

public sealed class GetServerCommand : AsyncCommand<GetServerCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--server-id|--server <SERVER>")]
        [Description("Server name or ID")]
        public required string ServerId { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var serverId = await Resolve.ServerAsync(client, settings.ServerId);
        var result = await client.GetAsync($"servers/{serverId}");
        Output.Write(result);
        return 0;
    }
}
