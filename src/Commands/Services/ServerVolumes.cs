using System.Text.Json;
using Sliplane.Console.Infrastructure;

namespace Sliplane.Console.Commands.Services;

internal static class ServerVolumes
{
    /// <summary>
    /// Maps volume names on a server to their ids.
    ///
    /// Sending a name to the API creates a volume - every time, even when one of that
    /// name already exists. Two services asked for "app-data" end up on two different
    /// volumes with the same name, which is invisible until one of them is missing its
    /// data. So callers resolve the name to an existing id first and only fall back to
    /// creating when there genuinely is none.
    /// </summary>
    public static async Task<Dictionary<string, string>> ByNameAsync(SliplaneClient client, string serverId)
    {
        var byName = new Dictionary<string, string>(StringComparer.Ordinal);
        var existing = await client.GetAsync($"servers/{serverId}/volumes");
        if (existing.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in existing.RootElement.EnumerateArray())
            {
                if (e.TryGetProperty("name", out var n) && e.TryGetProperty("id", out var i))
                {
                    var name = n.GetString();
                    // First wins, so a re-run keeps using the same volume.
                    if (name is not null && !byName.ContainsKey(name))
                        byName[name] = i.GetString()!;
                }
            }
        }
        return byName;
    }
}
