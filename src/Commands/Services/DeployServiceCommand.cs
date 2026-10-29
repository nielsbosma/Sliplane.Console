using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Services;

public sealed class DeployServiceCommand : AsyncCommand<DeployServiceCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--project-id|--project <PROJECT>")]
        [Description("Project name or ID (optional: the service is looked up in every project)")]
        public string? ProjectId { get; init; }

        [CommandOption("--service-id|--service <SERVICE>")]
        [Description("Service name or ID to deploy")]
        public required string ServiceId { get; init; }

        [CommandOption("--tag <TAG>")]
        [Description("Image tag to deploy (for image-based services)")]
        public string? Tag { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var (projectId, serviceId) = await Resolve.ServiceAsync(client, settings.ProjectId, settings.ServiceId);
        // An absent body means no Content-Type either, and the deploy endpoint
        // rejects that with "Invalid request body". An empty object is accepted.
        object body = !string.IsNullOrEmpty(settings.Tag) ? new { tag = settings.Tag } : new { };
        await client.PostAsync($"projects/{projectId}/services/{serviceId}/deploy", body);
        AnsiConsole.MarkupLine("[green]Deployment request accepted.[/]");
        return 0;
    }
}
