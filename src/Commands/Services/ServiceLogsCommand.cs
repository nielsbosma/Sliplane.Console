using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Sliplane.Console.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Services;

public sealed class ServiceLogsCommand : AsyncCommand<ServiceLogsCommand.Settings>
{
    /// <summary>The API returns at most this many lines per call; a full page may hide older ones.</summary>
    private const int PageLimit = 500;

    public sealed class Settings : ApiSettings
    {
        [CommandOption("--project-id|--project <PROJECT>")]
        [Description("Project name or ID (optional: the service is looked up in every project)")]
        public string? ProjectId { get; init; }

        [CommandOption("--service-id|--service <SERVICE>")]
        [Description("Service name or ID")]
        public required string ServiceId { get; init; }

        [CommandOption("--from <TIMESTAMP>")]
        [Description("From timestamp (Unix seconds)")]
        public long? From { get; init; }

        [CommandOption("--to <TIMESTAMP>")]
        [Description("To timestamp (Unix seconds)")]
        public long? To { get; init; }

        [CommandOption("--since <WHEN>")]
        [Description("Start at a lookback (30s, 15m, 2h, 1d) or an ISO 8601 time. Not with --from")]
        public string? Since { get; init; }

        [CommandOption("-f|--follow")]
        [Description("Keep polling and print new lines as they arrive, oldest first, until Ctrl+C")]
        public bool Follow { get; init; }

        [CommandOption("--interval <SECONDS>")]
        [Description("Polling interval with --follow (default 2, minimum 1)")]
        public int Interval { get; init; } = 2;

        public override ValidationResult Validate()
        {
            if (Since is not null && From.HasValue) return ValidationResult.Error("--since and --from both set a start; pass one.");
            if (Follow && To.HasValue) return ValidationResult.Error("--follow has no end; drop --to.");
            if (Interval < 1) return ValidationResult.Error("--interval must be at least 1 second.");
            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var (projectId, serviceId) = await Resolve.ServiceAsync(client, settings.ProjectId, settings.ServiceId);
        var from = settings.From ?? (settings.Since is null ? null : ParseSince(settings.Since));
        var path = $"projects/{projectId}/services/{serviceId}/logs";

        if (!settings.Follow)
        {
            var result = await client.GetAsync(path + Query(from, settings.To));
            WarnIfFull(result.RootElement);
            Output.Write(result);
            return 0;
        }

        using var cts = new CancellationTokenSource();
        System.Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        // Lines are keyed by (time, message) with a count, so a poll that overlaps the previous one
        // prints nothing twice while identical lines logged in the same instant still all appear.
        var seen = new Dictionary<(string, string), int>();
        string? latest = null;

        while (!cts.IsCancellationRequested)
        {
            using var doc = await client.GetAsync(path + Query(from, null));
            WarnIfFull(doc.RootElement);
            var lines = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : [];
            lines.Sort((a, b) => string.CompareOrdinal(Str(a, "createdAt"), Str(b, "createdAt")));

            var counts = new Dictionary<(string, string), int>();
            foreach (var line in lines)
            {
                var key = (Str(line, "createdAt"), Str(line, "message"));
                counts[key] = counts.GetValueOrDefault(key) + 1;
                if (counts[key] > seen.GetValueOrDefault(key)) Print(line);
                if (latest is null || string.CompareOrdinal(key.Item1, latest) > 0) latest = key.Item1;
            }
            foreach (var (key, count) in counts)
                if (count > seen.GetValueOrDefault(key)) seen[key] = count;

            // A one-second overlap covers the API's whole-second 'from'.
            if (latest is not null && DateTimeOffset.TryParse(latest, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t))
                from = t.ToUnixTimeSeconds() - 1;

            try { await Task.Delay(TimeSpan.FromSeconds(settings.Interval), cts.Token); }
            catch (TaskCanceledException) { }
        }
        return 0;
    }

    private static void Print(JsonElement line)
    {
        if (Output.UseJson)
        {
            System.Console.WriteLine(JsonSerializer.Serialize(line));
            return;
        }
        System.Console.WriteLine($"{Str(line, "createdAt")} {Str(line, "message").TrimEnd('\n', '\r')}");
    }

    private static void WarnIfFull(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() >= PageLimit)
            System.Console.Error.WriteLine($"warning: reached the API's {PageLimit}-line limit; older lines in this window are missing. Narrow it with --since/--from.");
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string Query(long? from, long? to)
    {
        var parts = new List<string>();
        if (from.HasValue) parts.Add($"from={from}");
        if (to.HasValue) parts.Add($"to={to}");
        return parts.Count > 0 ? "?" + string.Join("&", parts) : "";
    }

    /// <summary>A lookback such as 30m, or an absolute ISO 8601 time; returns Unix seconds.</summary>
    internal static long ParseSince(string value)
    {
        var v = value.Trim();
        if (v.Length > 1 && double.TryParse(v[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n > 0)
        {
            TimeSpan? span = char.ToLowerInvariant(v[^1]) switch
            {
                's' => TimeSpan.FromSeconds(n), 'm' => TimeSpan.FromMinutes(n),
                'h' => TimeSpan.FromHours(n), 'd' => TimeSpan.FromDays(n), _ => null
            };
            if (span is not null) return DateTimeOffset.UtcNow.Add(-span.Value).ToUnixTimeSeconds();
        }
        if (DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
            return at.ToUnixTimeSeconds();
        throw SliplaneException.Invalid($"--since '{value}' is neither a lookback (30m, 2h, 1d) nor an ISO 8601 time.");
    }
}
