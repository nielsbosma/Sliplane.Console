using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Servers;

public sealed class ServerMetricsCommand : AsyncCommand<ServerMetricsCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--server-id|--server <SERVER>")]
        [Description("Server name or ID")]
        public required string ServerId { get; init; }

        [CommandOption("--range <RANGE>")]
        [Description("Predefined time range: 10min, 1h, 24h, 7d (default: 1h). Not with --from/--to")]
        public string? Range { get; init; }

        [CommandOption("--from <TIMESTAMP>")]
        [Description("From timestamp (Unix seconds)")]
        public long? From { get; init; }

        [CommandOption("--to <TIMESTAMP>")]
        [Description("To timestamp (Unix seconds)")]
        public long? To { get; init; }

        [CommandOption("-w|--watch")]
        [Description("Redraw live charts every 5 s until Ctrl+C (terminal only; not with --from/--to)")]
        public bool Watch { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var serverId = await Resolve.ServerAsync(client, settings.ServerId);
        var query = MetricsQuery.Build(settings.Range, settings.From, settings.To);
        if (settings.Watch)
        {
            if (settings.From.HasValue || settings.To.HasValue)
                throw SliplaneException.Invalid("--watch shows a moving window; drop --from/--to.");
            await MetricsWatch.RunAsync("Server " + settings.ServerId, () => client.GetAsync($"servers/{serverId}/metrics{query}"), TimeSpan.FromSeconds(5));
            return 0;
        }
        var result = await client.GetAsync($"servers/{serverId}/metrics{query}");
        Output.Write(result);
        return 0;
    }
}
