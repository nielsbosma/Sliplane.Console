using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Npgsql;
using Sliplane.Console.Infrastructure;

namespace Sliplane.Console.Commands.Postgres;

/// <summary>
/// Connecting to a managed database directly: credentials come from the API (never typed or
/// printed), and the caller's public IP is checked against the database's allow list first,
/// because a blocked connection otherwise just times out with nothing saying why.
/// </summary>
internal static class PostgresDb
{
    public static async Task<NpgsqlDataSource> OpenAsync(SliplaneClient client, ApiSettings settings, string postgresId, bool allowMyIp)
    {
        using var doc = await client.GetAsync($"postgres/{postgresId}");
        var db = doc.RootElement;
        var name = db.TryGetProperty("name", out var n) ? n.GetString() : postgresId;

        if (db.TryGetProperty("status", out var status) && status.GetString() is "paused" or "suspended" or "deleting" or "stopped" &&
            status.GetString() is { } s)
            throw new SliplaneException(ErrorCode.Error, $"Database '{name}' is {s}, not live.",
                remediation: s == "paused" ? $"sliplane postgres unpause --postgres {name} -a {settings.ResolvedAccount?.Name}" : null);

        if (!db.TryGetProperty("connection", out var c) || c.ValueKind != JsonValueKind.Object)
            throw new SliplaneException(ErrorCode.Error, "The API returned no connection details for this database.");

        var host = c.GetProperty("host").GetString()!;
        await EnsureAccessAsync(client, settings, postgresId, name!, host, db, allowMyIp);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = c.GetProperty("port").GetInt32(),
            Database = c.GetProperty("database").GetString(),
            Username = c.GetProperty("user").GetString(),
            Password = c.GetProperty("password").GetString(),
            SslMode = SslMode.VerifyFull,
            Timeout = 15,
            ApplicationName = "sliplane-console"
        };
        return NpgsqlDataSource.Create(builder.ConnectionString);
    }

    private static async Task EnsureAccessAsync(SliplaneClient client, ApiSettings settings, string postgresId, string name,
        string host, JsonElement db, bool allowMyIp)
    {
        var rules = AllowList(db);

        IPAddress[] hostAddresses;
        try { hostAddresses = await Dns.GetHostAddressesAsync(host); }
        catch (SocketException) { return; } // let the connection report the DNS failure

        var families = hostAddresses.Select(a => a.AddressFamily).Distinct().ToList();
        if (families.All(f => rules.Any(r => CoversFamily(r.Source, f)))) return;

        var blocked = new List<IPAddress>();
        foreach (var family in families)
        {
            var ip = await client.ClientIpAsync(family);
            if (ip is not null && !rules.Any(r => Contains(r.Source, ip))) blocked.Add(ip);
        }
        if (blocked.Count == 0) return;

        var list = string.Join(", ", blocked);
        var command = $"sliplane postgres allowlist add --postgres {name} --source {blocked[0]} -a {settings.ResolvedAccount?.Name}";
        var ask = !allowMyIp && Interactive.IsTerminal &&
                  Spectre.Console.AnsiConsole.Confirm($"Your IP ({list}) is not on {name}'s allow list. Add it (it stays until removed)?", false);
        if (!allowMyIp && !ask)
            throw SliplaneException.Auth($"Your IP ({list}) is not on {name}'s allow list, so the connection would be refused.",
                remediation: $"{command}   (or pass --allow-my-ip)");

        foreach (var ip in blocked)
            rules.Add(new Rule(ip + (ip.AddressFamily == AddressFamily.InterNetwork ? "/32" : "/128"), $"sliplane-console {Environment.MachineName}"));
        await SaveAsync(client, postgresId, rules);
        System.Console.Error.WriteLine($"Added {list} to {name}'s allow list.");
    }

    public sealed record Rule(string Source, string? Description, string? Id = null);

    public static List<Rule> AllowList(JsonElement db) =>
        db.TryGetProperty("ipAllowList", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(r => new Rule(
                r.GetProperty("source").GetString()!,
                r.TryGetProperty("description", out var d) ? d.GetString() : null,
                r.TryGetProperty("id", out var i) ? i.GetString() : null)).ToList()
            : [];

    public static async Task<JsonDocument> SaveAsync(SliplaneClient client, string postgresId, IEnumerable<Rule> rules) =>
        await client.PatchAsync($"postgres/{postgresId}", new Dictionary<string, object>
        {
            ["ipAllowList"] = rules.Select(r =>
            {
                var item = new Dictionary<string, object> { ["source"] = r.Source };
                if (!string.IsNullOrEmpty(r.Description)) item["description"] = r.Description;
                return item;
            }).ToList()
        });

    public static string NormalizeSource(string value)
    {
        var v = value.Trim();
        if (v.Contains('/'))
        {
            if (!IPNetwork.TryParse(v, out var net)) throw SliplaneException.Invalid($"'{value}' is not an IP address or CIDR range.");
            return net.ToString();
        }
        if (!IPAddress.TryParse(v, out var ip)) throw SliplaneException.Invalid($"'{value}' is not an IP address or CIDR range.");
        return ip + (ip.AddressFamily == AddressFamily.InterNetwork ? "/32" : "/128");
    }

    private static bool CoversFamily(string source, AddressFamily family) =>
        IPNetwork.TryParse(source, out var net) && net.PrefixLength == 0 && net.BaseAddress.AddressFamily == family;

    private static bool Contains(string source, IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPNetwork.TryParse(source, out var net)) return net.Contains(ip);
        return IPAddress.TryParse(source, out var single) && single.Equals(ip);
    }

    /// <summary>A database value as something YAML/JSON output can carry without surprises.</summary>
    public static object? Value(object? value) => value switch
    {
        null or DBNull => null,
        DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("o", CultureInfo.InvariantCulture),
        byte[] bytes => Convert.ToBase64String(bytes),
        bool or string or int or long or short or decimal or double or float => value,
        Guid g => g.ToString(),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };
}
