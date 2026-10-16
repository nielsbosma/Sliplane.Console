using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Services;

public sealed class AttachVolumeCommand : AsyncCommand<AttachVolumeCommand.Settings>
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

        var body = new Dictionary<string, object> { ["mountPath"] = mountPath };
        if (!string.IsNullOrEmpty(settings.VolumeId))
        {
            body["id"] = settings.VolumeId;
        }
        else
        {
            // The API creates a new volume for every name it is sent; see ServerVolumes.
            var service = await client.GetAsync($"projects/{settings.ProjectId}/services/{settings.ServiceId}");
            var serverId = service.RootElement.GetProperty("serverId").GetString()!;
            var byName = await ServerVolumes.ByNameAsync(client, serverId);
            if (byName.TryGetValue(settings.Name!, out var id))
                body["id"] = id;
            else
                body["name"] = settings.Name!;
        }

        var result = await client.PostAsync(
            $"projects/{settings.ProjectId}/services/{settings.ServiceId}/volumes", body);
        Output.Write(result);
        return 0;
    }
}
