using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Servers;

public sealed class RenameServerVolumeCommand : AsyncCommand<RenameServerVolumeCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--server-id <ID>")]
        [Description("The ID of the server the volume lives on")]
        public required string ServerId { get; init; }

        [CommandOption("--volume-id <ID>")]
        [Description("The ID of the volume")]
        public required string VolumeId { get; init; }

        [CommandOption("--name <NAME>")]
        [Description("The new name of the volume")]
        public required string Name { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var result = await client.PatchAsync(
            $"servers/{settings.ServerId}/volumes/{settings.VolumeId}",
            new { name = settings.Name });
        Output.Write(result);
        return 0;
    }
}
