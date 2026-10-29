using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Services;

public sealed class AddDomainCommand : AsyncCommand<AddDomainCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--project-id|--project <PROJECT>")]
        [Description("Project name or ID (optional: the service is looked up in every project)")]
        public string? ProjectId { get; init; }

        [CommandOption("--service-id|--service <SERVICE>")]
        [Description("Service name or ID")]
        public required string ServiceId { get; init; }

        [CommandOption("--domain <DOMAIN>")]
        [Description("The custom domain to add")]
        public required string Domain { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var (projectId, serviceId) = await Resolve.ServiceAsync(client, settings.ProjectId, settings.ServiceId);
        var result = await client.PostAsync(
            $"projects/{projectId}/services/{serviceId}/domains",
            new { domain = settings.Domain });
        Output.Write(result);
        return 0;
    }
}
