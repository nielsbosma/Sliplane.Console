using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Services;

public sealed class ServiceMetricsCommand : AsyncCommand<ServiceMetricsCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--project-id|--project <PROJECT>")]
        [Description("Project name or ID (optional: the service is looked up in every project)")]
        public string? ProjectId { get; init; }

        [CommandOption("--service-id|--service <SERVICE>")]
        [Description("Service name or ID")]
        public required string ServiceId { get; init; }

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
        var (projectId, serviceId) = await Resolve.ServiceAsync(client, settings.ProjectId, settings.ServiceId);
        var query = MetricsQuery.Build(settings.Range, settings.From, settings.To);
        if (settings.Watch)
        {
            if (settings.From.HasValue || settings.To.HasValue)
                throw SliplaneException.Invalid("--watch shows a moving window; drop --from/--to.");
            await MetricsWatch.RunAsync("Service " + settings.ServiceId, () => client.GetAsync($"projects/{projectId}/services/{serviceId}/metrics{query}"), TimeSpan.FromSeconds(5));
            return 0;
        }
        var result = await client.GetAsync($"projects/{projectId}/services/{serviceId}/metrics{query}");
        Output.Write(result);
        return 0;
    }
}
