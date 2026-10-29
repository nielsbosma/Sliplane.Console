using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Projects;

public sealed class DeleteProjectCommand : AsyncCommand<DeleteProjectCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--project-id|--project <PROJECT>")]
        [Description("Project name or ID to delete")]
        public required string ProjectId { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var projectId = await Resolve.ProjectAsync(client, settings.ProjectId);
        settings.Confirm($"Delete project {projectId}?");
        await client.DeleteAsync($"projects/{projectId}");
        AnsiConsole.MarkupLine("[green]Project deleted.[/]");
        return 0;
    }
}
