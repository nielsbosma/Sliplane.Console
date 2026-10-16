using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Services;

public sealed class DetachSshKeyCommand : AsyncCommand<DetachSshKeyCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--project-id <ID>")]
        [Description("The ID of the project")]
        public required string ProjectId { get; init; }

        [CommandOption("--service-id <ID>")]
        [Description("The ID of the service")]
        public required string ServiceId { get; init; }

        [CommandOption("--key-id <ID>")]
        [Description("The ID of the SSH key to detach")]
        public required string KeyId { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        await client.DeleteAsync($"projects/{settings.ProjectId}/services/{settings.ServiceId}/ssh-keys/{settings.KeyId}");
        AnsiConsole.MarkupLine("[green]SSH key detached.[/]");
        return 0;
    }
}
