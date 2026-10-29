using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Services;

public sealed class DetachSshKeyCommand : AsyncCommand<DetachSshKeyCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--project-id|--project <PROJECT>")]
        [Description("Project name or ID (optional: the service is looked up in every project)")]
        public string? ProjectId { get; init; }

        [CommandOption("--service-id|--service <SERVICE>")]
        [Description("Service name or ID")]
        public required string ServiceId { get; init; }

        [CommandOption("--key-id <ID>")]
        [Description("The ID of the SSH key to detach")]
        public required string KeyId { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var (projectId, serviceId) = await Resolve.ServiceAsync(client, settings.ProjectId, settings.ServiceId);
        settings.Confirm($"Detach SSH key {settings.KeyId} from service {serviceId}?");
        await client.DeleteAsync($"projects/{projectId}/services/{serviceId}/ssh-keys/{settings.KeyId}");
        AnsiConsole.MarkupLine("[green]SSH key detached.[/]");
        return 0;
    }
}
