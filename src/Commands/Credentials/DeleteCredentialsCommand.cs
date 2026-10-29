using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Credentials;

public sealed class DeleteCredentialsCommand : AsyncCommand<DeleteCredentialsCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--credential-id|--credential <CREDENTIAL>")]
        [Description("Registry credentials name or ID to delete")]
        public required string CredentialId { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var credentialId = await Resolve.CredentialAsync(client, settings.CredentialId);
        settings.Confirm($"Delete registry credential {credentialId}?");
        await client.DeleteAsync($"registry-credentials/{credentialId}");
        AnsiConsole.MarkupLine("[green]Credentials deleted.[/]");
        return 0;
    }
}
