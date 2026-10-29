using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Credentials;

public sealed class UpdateCredentialsCommand : AsyncCommand<UpdateCredentialsCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--credential-id|--credential <CREDENTIAL>")]
        [Description("Registry credentials name or ID to update")]
        public required string CredentialId { get; init; }

        [CommandOption("--name <NAME>")]
        [Description("The new name for the credentials")]
        public required string Name { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var credentialId = await Resolve.CredentialAsync(client, settings.CredentialId);
        var result = await client.PatchAsync($"registry-credentials/{credentialId}", new { name = settings.Name });
        Output.Write(result);
        return 0;
    }
}
