using System.ComponentModel;
using System.Net.Sockets;
using Sliplane.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands;

public sealed class MeCommand : AsyncCommand<MeCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--ip")]
        [Description("Print this machine's public IPv4/IPv6 as the API sees them, instead (what a database allow list must contain)")]
        public bool Ip { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        if (settings.Ip)
        {
            Output.WriteObject(new Dictionary<string, object?>
            {
                ["ipv4"] = (await client.ClientIpAsync(AddressFamily.InterNetwork))?.ToString(),
                ["ipv6"] = (await client.ClientIpAsync(AddressFamily.InterNetworkV6))?.ToString()
            });
            return 0;
        }
        var result = await client.GetAsync("me");
        Output.Write(result);
        return 0;
    }
}
