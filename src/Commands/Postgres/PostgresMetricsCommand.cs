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
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var query = MetricsQuery.Build(settings.Range, settings.From, settings.To);
        var result = await client.GetAsync($"postgres/{settings.PostgresId}/metrics{query}");
        Output.Write(result);
        return 0;
    }
}
