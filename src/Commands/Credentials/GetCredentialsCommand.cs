using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Credentials;

public sealed class GetCredentialsCommand : AsyncCommand<GetCredentialsCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--credential-id|--credential <CREDENTIAL>")]
        [Description("Registry credentials name or ID")]
        public required string CredentialId { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var credentialId = await Resolve.CredentialAsync(client, settings.CredentialId);
        var result = await client.GetAsync($"registry-credentials/{credentialId}");
        Output.Write(result);
        return 0;
    }
}
