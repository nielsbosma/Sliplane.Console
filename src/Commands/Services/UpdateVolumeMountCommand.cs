using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Services;

public sealed class UpdateVolumeMountCommand : AsyncCommand<UpdateVolumeMountCommand.Settings>
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
        [Description("The ID of a volume attached to the service")]
        public required string VolumeId { get; init; }

        [CommandOption("--mount-path <PATH>")]
        [Description("The new mount path in the container, e.g. /data")]
        public required string MountPath { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var mountPath = PathArg.Check(settings.MountPath, "--mount-path");
        var client = settings.CreateClient();
        var (projectId, serviceId) = await Resolve.ServiceAsync(client, settings.ProjectId, settings.ServiceId);
        var result = await client.PatchAsync(
            $"projects/{projectId}/services/{serviceId}/volumes/{settings.VolumeId}",
            new { mountPath });
        Output.Write(result);
        return 0;
    }
}
