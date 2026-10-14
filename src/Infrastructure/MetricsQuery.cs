namespace Sliplane.Console.Infrastructure;

/// <summary>
/// The time window for <c>servers metrics</c> and <c>services metrics</c>.
/// The API takes either a predefined <c>range</c> or a <c>from</c>/<c>to</c> pair and
/// refuses both together (HTTP 400). So the 1h default applies only when no window was
/// given at all; it must never be declared as the option's default, or it would be sent
/// alongside every <c>--from</c>/<c>--to</c>.
/// </summary>
public static class MetricsQuery
{
    public const string DefaultRange = "1h";

    public static string Build(string? range, long? from, long? to)
    {
        var hasTimestamps = from.HasValue || to.HasValue;
        if (hasTimestamps && !string.IsNullOrEmpty(range))
            throw SliplaneException.Invalid(
                "--range cannot be combined with --from/--to: the API takes one or the other.",
                "Drop --range to ask for an exact window, or drop --from/--to to use a predefined range.");

        var parts = new List<string>();
        if (hasTimestamps)
        {
            if (from.HasValue) parts.Add($"from={from}");
            if (to.HasValue) parts.Add($"to={to}");
        }
        else
        {
            parts.Add($"range={(string.IsNullOrEmpty(range) ? DefaultRange : range)}");
        }
        return "?" + string.Join("&", parts);
    }
}
