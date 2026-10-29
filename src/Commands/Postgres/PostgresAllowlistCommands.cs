using System.ComponentModel;
using System.Net.Sockets;
using Sliplane.Console.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Postgres;

/// <summary>
/// One rule at a time on the database's IP allow list. The API only takes the whole list, so
/// each command reads it, changes one entry and writes the lot back; 'postgres update --ip-allow'
/// remains the way to replace it wholesale.
/// </summary>
public sealed class ListPostgresAllowlistCommand : AsyncCommand<PostgresSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PostgresSettings settings)
    {
        var client = settings.CreateClient();
        var postgresId = await Resolve.PostgresAsync(client, settings.PostgresId);
        using var doc = await client.GetAsync($"postgres/{postgresId}");
        Output.WriteObject(doc.RootElement.TryGetProperty("ipAllowList", out var list) ? Json.ToObject(list)! : new List<object>());
        return 0;
    }
}

public sealed class AddPostgresAllowlistCommand : AsyncCommand<AddPostgresAllowlistCommand.Settings>
{
    public sealed class Settings : PostgresSettings
    {
        [CommandOption("--source <IP_OR_CIDR>")]
        [Description("IP address or CIDR range to allow. Omit with --my-ip")]
        public string? Source { get; init; }

        [CommandOption("--my-ip")]
        [Description("Allow this machine's public IPv4 (and IPv6, if it has one), as the API sees them")]
        public bool MyIp { get; init; }

        [CommandOption("--description <TEXT>")]
        [Description("Note shown next to the rule")]
        public string? Description { get; init; }

        public override ValidationResult Validate() =>
            (Source is null) == !MyIp ? ValidationResult.Success() : ValidationResult.Error("Pass exactly one of --source or --my-ip.");
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var postgresId = await Resolve.PostgresAsync(client, settings.PostgresId);

        var sources = new List<string>();
        if (settings.Source is not null) sources.Add(PostgresDb.NormalizeSource(settings.Source));
        else
        {
            foreach (var family in new[] { AddressFamily.InterNetwork, AddressFamily.InterNetworkV6 })
                if (await client.ClientIpAsync(family) is { } ip) sources.Add(PostgresDb.NormalizeSource(ip.ToString()));
            if (sources.Count == 0) throw new SliplaneException(ErrorCode.Network, "The API did not report this machine's IP.");
        }

        using var doc = await client.GetAsync($"postgres/{postgresId}");
        var rules = PostgresDb.AllowList(doc.RootElement);
        var added = sources.Where(s => rules.All(r => r.Source != s)).ToList();
        rules.AddRange(added.Select(s => new PostgresDb.Rule(s, settings.Description ?? (settings.MyIp ? $"sliplane-console {Environment.MachineName}" : null))));

        if (added.Count > 0) await PostgresDb.SaveAsync(client, postgresId, rules);
        Output.WriteObject(new Dictionary<string, object?> { ["added"] = added, ["alreadyAllowed"] = sources.Except(added).ToList() });
        return 0;
    }
}

public sealed class RemovePostgresAllowlistCommand : AsyncCommand<RemovePostgresAllowlistCommand.Settings>
{
    public sealed class Settings : PostgresSettings
    {
        [CommandOption("--source <IP_CIDR_OR_RULE_ID>")]
        [Description("The rule to remove: its source (IP or CIDR) or its id (pgfw_...)")]
        public required string Source { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var postgresId = await Resolve.PostgresAsync(client, settings.PostgresId);
        using var doc = await client.GetAsync($"postgres/{postgresId}");
        var rules = PostgresDb.AllowList(doc.RootElement);

        var match = settings.Source.StartsWith("pgfw_")
            ? rules.Where(r => r.Id == settings.Source).ToList()
            : rules.Where(r => r.Source == PostgresDb.NormalizeSource(settings.Source)).ToList();
        if (match.Count == 0)
            throw SliplaneException.NotFound($"No allow-list rule matches '{settings.Source}'.",
                "Rules: " + string.Join(", ", rules.Select(r => $"{r.Source} ({r.Id})")));

        settings.Confirm($"Remove {string.Join(", ", match.Select(m => m.Source))} from the allow list?");
        await PostgresDb.SaveAsync(client, postgresId, rules.Except(match));
        Output.WriteObject(new Dictionary<string, object?> { ["removed"] = match.Select(m => m.Source).ToList() });
        return 0;
    }
}

public sealed class ClearPostgresAllowlistCommand : AsyncCommand<PostgresSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PostgresSettings settings)
    {
        var client = settings.CreateClient();
        var postgresId = await Resolve.PostgresAsync(client, settings.PostgresId);
        settings.Confirm("Remove every rule? All public connections to the database will be refused.");
        await PostgresDb.SaveAsync(client, postgresId, []);
        Output.WriteObject(new Dictionary<string, object?> { ["cleared"] = true });
        return 0;
    }
}
