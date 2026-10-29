using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.SshKeys;

public sealed class DeleteSshKeyCommand : AsyncCommand<DeleteSshKeyCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--key-id <ID>")]
        [Description("The ID of the SSH key. Detach it from every service first")]
        public required string KeyId { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        settings.Confirm($"Delete SSH key {settings.KeyId}?");
        await client.DeleteAsync($"ssh-keys/{settings.KeyId}");
        AnsiConsole.MarkupLine("[green]SSH key deleted.[/]");
        return 0;
    }
}
