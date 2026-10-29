namespace Sliplane.Console.Infrastructure;

/// <summary>
/// Builds the API's env array from --env-file, --env and --secret-env, in that order; the last
/// value for a key wins. Values are literal: no quote removal, no interpolation.
/// </summary>
public static class EnvEntries
{
    public static List<Dictionary<string, object>> Build(string[]? files, string[]? plain, string[]? secret)
    {
        var entries = new List<(string Key, string Value, bool Secret)>();

        foreach (var file in files ?? [])
        {
            if (!File.Exists(file))
                throw SliplaneException.Invalid($"--env-file '{file}' does not exist.");
            var lineNo = 0;
            foreach (var raw in File.ReadAllLines(file))
            {
                lineNo++;
                var line = raw.TrimEnd('\r');
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#')) continue;
                entries.Add(Parse(line, $"{file}:{lineNo}", secret: true));
            }
        }
        foreach (var e in plain ?? []) entries.Add(Parse(e, "--env", secret: false));
        foreach (var e in secret ?? []) entries.Add(Parse(e, "--secret-env", secret: true));

        var byKey = new Dictionary<string, (string Value, bool Secret)>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var (key, value, isSecret) in entries)
        {
            if (!byKey.ContainsKey(key)) order.Add(key);
            byKey[key] = (value, isSecret);
        }

        return order.Select(k => new Dictionary<string, object>
        {
            ["key"] = k, ["value"] = byKey[k].Value, ["secret"] = byKey[k].Secret
        }).ToList();
    }

    /// <summary>
    /// <c>KEY=value</c> is literal; a bare <c>KEY</c> takes the value from this process's
    /// environment, so a secret never has to appear on the command line.
    /// </summary>
    public static (string Key, string Value, bool Secret) Parse(string entry, string source, bool secret)
    {
        var parts = entry.Split('=', 2);
        var key = parts[0].Trim();
        if (key.Length == 0)
            throw SliplaneException.Invalid($"{source}: an environment entry needs a key ('{entry}').");

        if (parts.Length == 2) return (key, parts[1], secret);

        var local = Environment.GetEnvironmentVariable(key);
        if (local is null)
            throw SliplaneException.Invalid(
                $"{source}: '{key}' has no value and is not set in your environment.",
                $"Pass {key}=<value>, or set {key} locally first.");
        return (key, local, secret);
    }
}
