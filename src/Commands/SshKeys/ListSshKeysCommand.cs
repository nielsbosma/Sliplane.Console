using Sliplane.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.SshKeys;

public sealed class ListSshKeysCommand : AsyncCommand<ApiSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ApiSettings settings)
    {
        var client = settings.CreateClient();
        var result = await client.GetAsync("ssh-keys");
        Output.Write(result);
        return 0;
    }
}
