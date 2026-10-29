using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Services;

public sealed class DetachVolumeCommand : AsyncCommand<DetachVolumeCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--project-id|--project <PROJECT>")]
        [Description("Project name or ID (optional: the service is looked up in every project)")]
        public string? ProjectId { get; init; }

        [CommandOption("--service-id|--service <SERVICE>")]
        [Description("Service name or ID")]
        public required string ServiceId { get; init; }

        [CommandOption("--volume-id <ID>")]
        [Description("The ID of the volume to detach")]
        public required string VolumeId { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var (projectId, serviceId) = await Resolve.ServiceAsync(client, settings.ProjectId, settings.ServiceId);
        settings.Confirm($"Detach volume {settings.VolumeId} from service {serviceId} and redeploy?");
        await client.DeleteAsync($"projects/{projectId}/services/{serviceId}/volumes/{settings.VolumeId}");
        AnsiConsole.MarkupLine("[green]Volume detached. Its data stays on the server; the service is redeploying.[/]");
        return 0;
    }
}
