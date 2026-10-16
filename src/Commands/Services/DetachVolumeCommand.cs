using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Services;

public sealed class DetachVolumeCommand : AsyncCommand<DetachVolumeCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--project-id <ID>")]
        [Description("The ID of the project")]
        public required string ProjectId { get; init; }

        [CommandOption("--service-id <ID>")]
        [Description("The ID of the service")]
        public required string ServiceId { get; init; }

        [CommandOption("--volume-id <ID>")]
        [Description("The ID of the volume to detach")]
        public required string VolumeId { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        await client.DeleteAsync($"projects/{settings.ProjectId}/services/{settings.ServiceId}/volumes/{settings.VolumeId}");
        AnsiConsole.MarkupLine("[green]Volume detached. Its data stays on the server; the service is redeploying.[/]");
        return 0;
    }
}
