using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sliplane.Console.Infrastructure;

namespace Sliplane.Console.Commands.Presets;

/// <summary>
/// The dashboard's one-click presets, read from Sliplane's public catalog (no credentials sent).
/// A preset carries placeholders such as <c>__SLIPLANE_GENERATED_3__</c>; each is replaced with
/// one fresh value per deployment, so a password shared by two env vars stays the same in both.
/// </summary>
internal static class PresetCatalog
{
    private const string Url = "https://sliplane.io/api/presets";

    public static async Task<JsonObject> FetchAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        string body;
        try { body = await http.GetStringAsync(Url); }
        catch (HttpRequestException ex) { throw new SliplaneException(ErrorCode.Network, "Could not fetch the preset catalog.", ex.Message); }

        var catalog = JsonNode.Parse(body) as JsonObject;
        if (catalog?["presets"] is not JsonArray { Count: > 0 })
            throw new SliplaneException(ErrorCode.Error, "The preset catalog is empty or unreadable.");
        return catalog;
    }

    public static IEnumerable<JsonObject> Presets(JsonObject catalog) => catalog["presets"]!.AsArray().OfType<JsonObject>();

    public static string Id(JsonObject preset) => preset["settings"]?["presetId"]?.GetValue<string>() ?? "";

    /// <summary>A copy of the preset with every generated placeholder filled.</summary>
    public static JsonObject Instantiate(JsonObject catalog, string id)
    {
        var matches = Presets(catalog).Where(p => Id(p) == id).ToList();
        if (matches.Count == 0)
            throw SliplaneException.NotFound($"No preset '{id}'.", "Run: sliplane presets list");
        if (matches.Count > 1)
            throw new SliplaneException(ErrorCode.Error, $"Preset '{id}' appears more than once in the catalog.");

        var text = matches[0].ToJsonString();
        var generators = catalog["generatedValues"] as JsonArray ?? [];
        for (var i = 0; i < generators.Count; i++)
        {
            var g = generators[i]!;
            var placeholder = g["placeholder"]!.GetValue<string>();
            if (placeholder != $"__SLIPLANE_GENERATED_{i}__")
                throw new SliplaneException(ErrorCode.Error, "The preset catalog has an invalid placeholder.");
            if (!text.Contains(placeholder)) continue;
            text = text.Replace(placeholder, Generate(g["kind"]!.GetValue<string>(), g["length"]!.GetValue<int>()));
        }
        if (text.Contains("__SLIPLANE_GENERATED_"))
            throw new SliplaneException(ErrorCode.Error, $"Preset '{id}' has placeholders the catalog does not define.");
        return (JsonObject)JsonNode.Parse(text)!;
    }

    private static string Generate(string kind, int length)
    {
        if (length is < 1 or > 128) throw new SliplaneException(ErrorCode.Error, "The preset catalog asks for an invalid value length.");
        switch (kind)
        {
            case "string":
                const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
                return RandomNumberGenerator.GetString(alphabet, length);
            case "hex":
                return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(length));
            case "uuid":
                return Guid.NewGuid().ToString();
            default:
                throw new SliplaneException(ErrorCode.Error, $"The preset catalog uses an unknown generator '{kind}'.");
        }
    }
}
