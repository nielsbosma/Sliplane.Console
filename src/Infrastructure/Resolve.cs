using System.Text.Json;
using Spectre.Console;

namespace Sliplane.Console.Infrastructure;

/// <summary>
/// Turns the name-or-id a caller typed into the id the API wants.
///
/// An exact id always wins. Otherwise exactly one resource must carry that exact name: two
/// services called "api" in different projects are an error naming both ids, never a guess,
/// because a guess is how a command lands on the wrong service and succeeds.
/// </summary>
public static class Resolve
{
    public sealed record Match(string Id, string Name, JsonElement Element);

    public static async Task<string> ProjectAsync(SliplaneClient client, string query) =>
        (await OneAsync(client, "projects", "project", query)).Id;

    /// <summary>For commands that create something: --project, else the account's saved default.</summary>
    public static async Task<string> ProjectOrDefaultAsync(SliplaneClient client, ApiSettings settings, string? project)
    {
        if (!string.IsNullOrWhiteSpace(project)) return await ProjectAsync(client, project);
        var saved = settings.ResolvedAccount?.Config.DefaultProject;
        if (!string.IsNullOrWhiteSpace(saved)) return saved;
        return await ChooseAsync(client, "projects", "project",
            $"Pass --project <name-or-id>, or save one: sliplane config set project <name> -a {settings.ResolvedAccount?.Name}");
    }

    /// <summary>For commands that create something: --server, else the only server, else ask in a terminal.</summary>
    public static async Task<string> ServerOrChooseAsync(SliplaneClient client, string? server) =>
        !string.IsNullOrWhiteSpace(server)
            ? await ServerAsync(client, server)
            : await ChooseAsync(client, "servers", "server", "Pass --server <name-or-id>.");

    /// <summary>
    /// Picks when nothing was given: the only candidate, or a choice in a terminal. Never a guess
    /// between several when nobody is there to answer.
    /// </summary>
    private static async Task<string> ChooseAsync(SliplaneClient client, string path, string kind, string remediation)
    {
        using var doc = await client.GetAsync(path);
        var items = Items(doc.RootElement).ToList();
        if (items.Count == 1) return items[0].Id;
        if (items.Count == 0) throw SliplaneException.Invalid($"There is no {kind} yet; create one first.");
        if (!Interactive.IsTerminal)
            throw SliplaneException.Invalid($"No {kind} given, and there are {items.Count} to choose from: {Describe(items)}.", remediation);

        var picked = AnsiConsole.Prompt(new SelectionPrompt<Match>()
            .Title($"Choose a {kind}")
            .UseConverter(m => Markup.Escape($"{m.Name} ({m.Id})"))
            .AddChoices(items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)));
        return picked.Id;
    }

    public static async Task<string> ServerAsync(SliplaneClient client, string query) =>
        (await OneAsync(client, "servers", "server", query)).Id;

    public static async Task<string> PostgresAsync(SliplaneClient client, string query) =>
        (await OneAsync(client, "postgres", "Postgres database", query)).Id;

    public static async Task<string> BucketAsync(SliplaneClient client, string query) =>
        (await OneAsync(client, "buckets", "bucket", query)).Id;

    public static async Task<string> CredentialAsync(SliplaneClient client, string query) =>
        (await OneAsync(client, "registry-credentials", "registry credential", query)).Id;

    public static async Task<string> SshKeyAsync(SliplaneClient client, string query) =>
        (await OneAsync(client, "ssh-keys", "SSH key", query)).Id;

    public static async Task<string> VolumeAsync(SliplaneClient client, string serverId, string query) =>
        (await OneAsync(client, $"servers/{serverId}/volumes", "volume", query)).Id;

    /// <summary>
    /// Finds a service by name or id. With a project, only that project is searched; without
    /// one, every project is, and a name that exists in two projects asks for --project.
    /// </summary>
    public static async Task<(string ProjectId, string ServiceId)> ServiceAsync(
        SliplaneClient client, string? project, string service)
    {
        var found = await ServiceMatchAsync(client, project, service);
        return (found.ProjectId, found.Service.Id);
    }

    public static async Task<(string ProjectId, Match Service)> ServiceMatchAsync(
        SliplaneClient client, string? project, string service)
    {
        if (string.IsNullOrWhiteSpace(service))
            throw SliplaneException.Invalid("No service given.", "Pass --service <name-or-id>.");

        List<string> projectIds;
        if (!string.IsNullOrWhiteSpace(project))
        {
            projectIds = [await ProjectAsync(client, project)];
        }
        else
        {
            using var projects = await client.GetAsync("projects");
            projectIds = Items(projects.RootElement).Select(p => p.Id).ToList();
        }

        var candidates = new List<(string ProjectId, Match Service)>();
        foreach (var projectId in projectIds)
        {
            var services = await client.GetAsync($"projects/{projectId}/services");
            candidates.AddRange(Items(services.RootElement).Select(s => (projectId, s)));
        }

        var byId = candidates.Where(c => c.Service.Id == service).ToList();
        if (byId.Count == 1) return byId[0];

        var byName = candidates.Where(c => c.Service.Name == service).ToList();
        if (byName.Count == 1) return byName[0];

        if (byName.Count == 0)
            throw SliplaneException.NotFound(
                $"No service named or with id '{service}'" + (project is null ? "." : $" in project '{project}'."),
                "Services: " + Describe(candidates.Select(c => c.Service)));

        throw SliplaneException.Invalid(
            $"Service name '{service}' is ambiguous: " +
            string.Join(", ", byName.Select(c => $"{c.Service.Id} (project {c.ProjectId})")) + ".",
            "Pass the service id, or narrow it with --project <name-or-id>.");
    }

    public static async Task<Match> OneAsync(SliplaneClient client, string path, string kind, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw SliplaneException.Invalid($"No {kind} given.");

        using var doc = await client.GetAsync(path);
        var items = Items(doc.RootElement).Select(m => m with { Element = m.Element.Clone() }).ToList();

        var byId = items.FirstOrDefault(i => i.Id == query);
        if (byId is not null) return byId;

        var byName = items.Where(i => i.Name == query).ToList();
        if (byName.Count == 1) return byName[0];

        if (byName.Count == 0)
            throw SliplaneException.NotFound($"No {kind} named or with id '{query}'.", $"Known: {Describe(items)}");

        throw SliplaneException.Invalid(
            $"{char.ToUpper(kind[0])}{kind[1..]} name '{query}' is ambiguous: {string.Join(", ", byName.Select(i => i.Id))}.",
            "Pass the id instead.");
    }

    private static IEnumerable<Match> Items(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array) yield break;
        foreach (var e in root.EnumerateArray())
        {
            var id = e.TryGetProperty("id", out var i) ? i.GetString() : null;
            if (id is null) continue;
            var name = e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            yield return new Match(id, name, e.Clone());
        }
    }

    private static string Describe(IEnumerable<Match> items)
    {
        var list = items.Select(i => $"{i.Name} ({i.Id})").ToList();
        return list.Count == 0 ? "(none)" : string.Join(", ", list);
    }
}
