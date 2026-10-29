using System.Text.Json;
using Spectre.Console;

namespace Sliplane.Console.Infrastructure;

/// <summary>
/// <c>metrics --watch</c>: re-asks the API every few seconds and redraws a sparkline per
/// numeric field until Ctrl+C. The fields are read from the response rather than hard-coded,
/// so services, servers and Postgres share it even though their metrics differ.
/// </summary>
public static class MetricsWatch
{
    private const string Bars = "▁▂▃▄▅▆▇█";

    public static async Task RunAsync(string title, Func<Task<JsonDocument>> fetch, TimeSpan interval)
    {
        if (!Interactive.IsTerminal)
            throw SliplaneException.Invalid("--watch needs an interactive terminal.", "Drop --watch to get the samples as YAML/JSON.");

        using var cts = new CancellationTokenSource();
        System.Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        await AnsiConsole.Live(new Markup("[grey]Loading metrics...[/]")).StartAsync(async live =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    using var doc = await fetch();
                    live.UpdateTarget(Render(title, doc.RootElement, interval));
                }
                catch (SliplaneException ex)
                {
                    live.UpdateTarget(new Markup($"[red]{Markup.Escape(ex.Message)}[/] [grey](retrying)[/]"));
                }
                try { await Task.Delay(interval, cts.Token); } catch (TaskCanceledException) { }
            }
        });
    }

    private static Table Render(string title, JsonElement samples, TimeSpan interval)
    {
        var points = samples.ValueKind == JsonValueKind.Array ? samples.EnumerateArray().ToList() : [];
        points.Sort((a, b) => string.CompareOrdinal(Time(a), Time(b)));

        var table = new Table().Border(TableBorder.Rounded)
            .Title($"[bold]{Markup.Escape(title)}[/]")
            .Caption($"[grey]{points.Count} samples, last {Markup.Escape(points.Count > 0 ? Time(points[^1]) : "-")} - refresh {interval.TotalSeconds:0}s, Ctrl+C to stop[/]");
        table.AddColumn("Metric");
        table.AddColumn(new TableColumn("Latest").RightAligned());
        table.AddColumn(new TableColumn("Max").RightAligned());
        table.AddColumn("History");

        var fields = points.SelectMany(p => p.EnumerateObject())
            .Where(p => p.Value.ValueKind == JsonValueKind.Number)
            .Select(p => p.Name).Distinct().ToList();

        var width = Math.Max(10, Math.Min(80, System.Console.WindowWidth - 50));
        foreach (var field in fields)
        {
            var values = points.Select(p => p.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.NaN)
                .Where(v => !double.IsNaN(v)).ToList();
            if (values.Count == 0) continue;
            table.AddRow(Markup.Escape(field), Format(field, values[^1]), Format(field, values.Max()),
                $"[cyan]{Sparkline(values, width)}[/]");
        }
        return table;
    }

    private static string Time(JsonElement e) =>
        e.TryGetProperty("createdAt", out var t) ? t.GetString() ?? "" : e.TryGetProperty("timestamp", out var u) ? u.ToString() : "";

    private static string Format(string field, double value)
    {
        if (field.Contains("memory", StringComparison.OrdinalIgnoreCase) || field.Contains("disk", StringComparison.OrdinalIgnoreCase) ||
            field.Contains("bytes", StringComparison.OrdinalIgnoreCase))
        {
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            var u = 0;
            while (value >= 1024 && u < units.Length - 1) { value /= 1024; u++; }
            return $"{value:0.#} {units[u]}";
        }
        if (field.Contains("cpu", StringComparison.OrdinalIgnoreCase)) return $"{value:0.##}%";
        return value % 1 == 0 ? $"{value:0}" : $"{value:0.##}";
    }

    /// <summary>Downsamples to <paramref name="width"/> buckets by taking each bucket's max, so a spike stays visible.</summary>
    private static string Sparkline(List<double> values, int width)
    {
        var buckets = new List<double>();
        var per = Math.Max(1.0, values.Count / (double)width);
        for (double i = 0; i < values.Count; i += per)
            buckets.Add(values.Skip((int)i).Take(Math.Max(1, (int)Math.Ceiling(per))).Max());

        var min = buckets.Min();
        var max = buckets.Max();
        var span = max - min;
        return string.Concat(buckets.Select(v => Bars[span <= 0 ? 0 : (int)Math.Round((v - min) / span * (Bars.Length - 1))]));
    }
}
