using System.ComponentModel;
using System.Text.Json;
using Sliplane.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Services;

public sealed class ListServiceDomainsCommand : AsyncCommand<ListServiceDomainsCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--project-id|--project <PROJECT>")]
        [Description("Project name or ID (optional: the service is looked up in every project)")]
        public string? ProjectId { get; init; }

        [CommandOption("--service-id|--service <SERVICE>")]
        [Description("Service name or ID")]
        public required string ServiceId { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var (_, service) = await Resolve.ServiceMatchAsync(client, settings.ProjectId, settings.ServiceId);
        var network = service.Element.TryGetProperty("network", out var n) ? n : default;

        Output.WriteObject(new Dictionary<string, object?>
        {
            ["service"] = service.Name,
            ["managedDomain"] = Str(network, "managedDomain"),
            ["internalDomain"] = Str(network, "internalDomain"),
            ["customDomains"] = network.ValueKind == JsonValueKind.Object && network.TryGetProperty("customDomains", out var c)
                ? Json.ToObject(c) : new List<object>()
        });
        return 0;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.GetString() : null;
}
