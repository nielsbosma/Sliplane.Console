using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Postgres;

public sealed class PostgresMetricsCommand : AsyncCommand<PostgresMetricsCommand.Settings>
{
    public sealed class Settings : PostgresSettings
    {
        [CommandOption("--range <RANGE>")]
        [Description("Predefined time range: 10min, 1h, 24h (default: 1h). Not with --from/--to")]
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
        var postgresId = await Resolve.PostgresAsync(client, settings.PostgresId);
        var query = MetricsQuery.Build(settings.Range, settings.From, settings.To);
        if (settings.Watch)
        {
            if (settings.From.HasValue || settings.To.HasValue)
                throw SliplaneException.Invalid("--watch shows a moving window; drop --from/--to.");
            await MetricsWatch.RunAsync("Postgres " + settings.PostgresId, () => client.GetAsync($"postgres/{postgresId}/metrics{query}"), TimeSpan.FromSeconds(5));
            return 0;
        }
        var result = await client.GetAsync($"postgres/{postgresId}/metrics{query}");
        Output.Write(result);
        return 0;
    }
}
