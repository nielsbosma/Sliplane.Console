using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Services;

public sealed class AttachVolumeCommand : AsyncCommand<AttachVolumeCommand.Settings>
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
        [Description("The ID of an existing volume on the service's server. Not with --name")]
        public string? VolumeId { get; init; }

        [CommandOption("--name <NAME>")]
        [Description("A volume name: reuses the server's volume of that name, or creates one. Not with --volume-id")]
        public string? Name { get; init; }

        [CommandOption("--mount-path <PATH>")]
        [Description("Where to mount the volume in the container, e.g. /data")]
        public required string MountPath { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrEmpty(VolumeId) == string.IsNullOrEmpty(Name))
                return ValidationResult.Error("Give exactly one of --volume-id or --name.");
            return base.Validate();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var mountPath = PathArg.Check(settings.MountPath, "--mount-path");
        var client = settings.CreateClient();
        var (projectId, serviceId) = await Resolve.ServiceAsync(client, settings.ProjectId, settings.ServiceId);

        var body = new Dictionary<string, object> { ["mountPath"] = mountPath };
        if (!string.IsNullOrEmpty(settings.VolumeId))
        {
            body["id"] = settings.VolumeId;
        }
        else
        {
            // The API creates a new volume for every name it is sent; see ServerVolumes.
            var service = await client.GetAsync($"projects/{projectId}/services/{serviceId}");
            var serverId = service.RootElement.GetProperty("serverId").GetString()!;
            var byName = await ServerVolumes.ByNameAsync(client, serverId);
            if (byName.TryGetValue(settings.Name!, out var id))
                body["id"] = id;
            else
                body["name"] = settings.Name!;
        }

        var result = await client.PostAsync(
            $"projects/{projectId}/services/{serviceId}/volumes", body);
        Output.Write(result);
        return 0;
    }
}
