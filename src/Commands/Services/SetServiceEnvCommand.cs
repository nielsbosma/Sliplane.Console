using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Services;

public sealed class SetServiceEnvCommand : AsyncCommand<SetServiceEnvCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--project-id|--project <PROJECT>")]
        [Description("Project name or ID (optional: the service is looked up in every project)")]
        public string? ProjectId { get; init; }

        [CommandOption("--service-id|--service <SERVICE>")]
        [Description("Service name or ID")]
        public required string ServiceId { get; init; }

        [CommandOption("--key <KEY>")]
        [Description("The environment variable key")]
        public required string Key { get; init; }

        [CommandOption("--value <VALUE>")]
        [Description("The environment variable value")]
        public required string Value { get; init; }

        [CommandOption("--secret")]
        [Description("Mask the value in API responses")]
        public bool Secret { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var (projectId, serviceId) = await Resolve.ServiceAsync(client, settings.ProjectId, settings.ServiceId);
        var result = await client.PutAsync(
            $"projects/{projectId}/services/{serviceId}/env/{Uri.EscapeDataString(settings.Key)}",
            new { value = settings.Value, secret = settings.Secret });
        Output.Write(result);
        return 0;
    }
}
