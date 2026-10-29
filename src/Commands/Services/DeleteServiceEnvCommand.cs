using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Services;

public sealed class DeleteServiceEnvCommand : AsyncCommand<DeleteServiceEnvCommand.Settings>
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
        [Description("The environment variable key to delete")]
        public required string Key { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var (projectId, serviceId) = await Resolve.ServiceAsync(client, settings.ProjectId, settings.ServiceId);
        settings.Confirm($"Delete {settings.Key} from service {serviceId}?");
        await client.DeleteAsync(
            $"projects/{projectId}/services/{serviceId}/env/{Uri.EscapeDataString(settings.Key)}");
        AnsiConsole.MarkupLine("[green]Environment variable deleted.[/]");
        return 0;
    }
}
