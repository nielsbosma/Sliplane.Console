using System.ComponentModel;
using System.Text.Json.Nodes;
using Sliplane.Console.Commands.Services;
using Sliplane.Console.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Presets;

public sealed class ListPresetsCommand : AsyncCommand<ListPresetsCommand.Settings>
{
    public sealed class Settings : LocalSettings
    {
        [CommandOption("--tag <TAG>")]
        [Description("Only presets with this tag (e.g. database, automation)")]
        public string? Tag { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        settings.ApplyOutputMode();
        var catalog = await PresetCatalog.FetchAsync();
        var list = PresetCatalog.Presets(catalog)
            .Where(p => settings.Tag is null || (p["tags"] as JsonArray ?? []).Any(t => string.Equals(t?.GetValue<string>(), settings.Tag, StringComparison.OrdinalIgnoreCase)))
            .Select(p => new Dictionary<string, object?>
            {
                ["id"] = PresetCatalog.Id(p),
                ["name"] = p["name"]?.GetValue<string>(),
                ["image"] = p["settings"]?["externalDockerImage"]?.GetValue<string>(),
                ["tags"] = (p["tags"] as JsonArray ?? []).Select(t => t?.GetValue<string>()).ToList(),
                ["minMemoryMb"] = p["requirements"]?["min"]?["memory"]?.GetValue<int>(),
                ["description"] = p["description"]?.GetValue<string>()
            })
            .OrderBy(p => p["id"] as string, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Output.WriteObject(list);
        return 0;
    }
}

/// <summary>
/// Creates a service from a preset, as the dashboard's one-click deploy does. Every run creates
/// a new service with freshly generated names and credentials.
/// </summary>
public sealed class DeployPresetCommand : AsyncCommand<DeployPresetCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandArgument(0, "<PRESET>")]
        [Description("Preset id from 'sliplane presets list' (e.g. n8n)")]
        public required string Preset { get; init; }

        [CommandOption("--project-id|--project <PROJECT>")]
        [Description("Project name or ID (default: 'sliplane config set project')")]
        public string? ProjectId { get; init; }

        [CommandOption("--server-id|--server <SERVER>")]
        [Description("Server name or ID to run the service on (default: the only server, or asked in a terminal)")]
        public string? ServerId { get; init; }

        [CommandOption("--name <NAME>")]
        [Description("Service name (default: the preset's generated name)")]
        public string? Name { get; init; }

        [CommandOption("--env <KEY=VALUE>")]
        [Description("Override or add an environment variable: KEY=value, or KEY to copy it from your environment; stored as secret (repeatable)")]
        public string[]? Env { get; init; }

        [CommandOption("--show-secrets")]
        [Description("Print generated secret values too. They cannot be read back later: secret env reads back empty")]
        public bool ShowSecrets { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var catalog = await PresetCatalog.FetchAsync();
        var preset = PresetCatalog.Instantiate(catalog, settings.Preset);
        var s = preset["settings"]!.AsObject();

        if (s["deploySource"]?.GetValue<string>() != "registry" || string.IsNullOrEmpty(s["externalDockerImage"]?.GetValue<string>()))
            throw new SliplaneException(ErrorCode.Error, $"Preset '{settings.Preset}' is not an image preset this CLI can deploy.");

        var projectId = await Resolve.ProjectOrDefaultAsync(client, settings, settings.ProjectId);
        var serverId = await Resolve.ServerOrChooseAsync(client, settings.ServerId);

        // Environment: the preset's, then overrides by key.
        var env = new List<(string Key, string Value, bool Secret)>();
        foreach (var e in s["envs"] as JsonArray ?? [])
            env.Add((e!["key"]!.GetValue<string>(), e["value"]?.GetValue<string>() ?? "", e["secret"]?.GetValue<bool>() ?? false));
        foreach (var raw in settings.Env ?? [])
        {
            var o = EnvEntries.Parse(raw, "--env", secret: true);
            var i = env.FindIndex(x => x.Key == o.Key);
            if (i >= 0) env[i] = o; else env.Add(o);
        }
        var missing = env.Where(e => e.Value.Length == 0).Select(e => e.Key).ToList();
        if (missing.Count > 0)
            throw SliplaneException.Invalid($"Preset '{settings.Preset}' needs values for: {string.Join(", ", missing)}.",
                string.Join(" ", missing.Select(k => $"--env {k}=<value>")));

        var protocol = s["protocol"]?.GetValue<string>() ?? "http";
        var isPublic = s["public"]?.GetValue<bool>() ?? false;
        var body = new Dictionary<string, object>
        {
            ["name"] = settings.Name ?? s["name"]!.GetValue<string>(),
            ["serverId"] = serverId,
            ["deployment"] = new Dictionary<string, object> { ["url"] = s["externalDockerImage"]!.GetValue<string>() },
            ["network"] = new Dictionary<string, object> { ["public"] = isPublic, ["protocol"] = protocol },
            ["env"] = env.Select(e => new Dictionary<string, object> { ["key"] = e.Key, ["value"] = e.Value, ["secret"] = e.Secret }).ToList()
        };
        if (protocol == "http" && s["healthcheckPath"]?.GetValue<string>() is { Length: > 0 } hc) body["healthcheck"] = hc;
        if (s["cmd"]?.GetValue<string>() is { Length: > 0 } cmd) body["cmd"] = cmd;

        var volumes = new List<Dictionary<string, object>>();
        var existing = await ServerVolumes.ByNameAsync(client, serverId);
        foreach (var mount in s["volumes"] as JsonArray ?? [])
        {
            var volumeId = mount!["id"]!.GetValue<string>();
            var path = mount["containerMountPath"]!.GetValue<string>();
            var name = (preset["volumes"] as JsonArray ?? []).FirstOrDefault(v => v?["id"]?.GetValue<string>() == volumeId)?["name"]?.GetValue<string>();
            if (name is null || !path.StartsWith('/'))
                throw new SliplaneException(ErrorCode.Error, $"Preset '{settings.Preset}' has an invalid volume mount.");
            volumes.Add(existing.TryGetValue(name, out var id)
                ? new Dictionary<string, object> { ["id"] = id, ["mountPath"] = path }
                : new Dictionary<string, object> { ["name"] = name, ["mountPath"] = path });
        }
        if (volumes.Count > 0) body["volumes"] = volumes;

        using var created = await client.PostAsync($"projects/{projectId}/services", body);
        var root = created.RootElement;
        Output.WriteObject(new Dictionary<string, object?>
        {
            ["id"] = root.TryGetProperty("id", out var i2) ? i2.GetString() : null,
            ["name"] = root.TryGetProperty("name", out var n) ? n.GetString() : null,
            ["status"] = root.TryGetProperty("status", out var st) ? st.GetString() : null,
            ["preset"] = settings.Preset,
            ["projectId"] = projectId,
            ["serverId"] = serverId,
            ["network"] = root.TryGetProperty("network", out var net) ? Json.ToObject(net) : null,
            ["env"] = env.Select(e => new Dictionary<string, object?>
            {
                ["key"] = e.Key, ["secret"] = e.Secret,
                ["value"] = e.Secret && !settings.ShowSecrets ? "(hidden; --show-secrets to print)" : e.Value
            }).ToList(),
            ["next"] = "Deployment has started. Follow it with: sliplane services logs --service <name> -f"
        });
        return 0;
    }
}
