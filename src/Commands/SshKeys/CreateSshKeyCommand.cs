using System.ComponentModel;
using System.Globalization;
using Sliplane.Console.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.SshKeys;

public sealed class CreateSshKeyCommand : AsyncCommand<CreateSshKeyCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--name <NAME>")]
        [Description("A name for the key")]
        public required string Name { get; init; }

        [CommandOption("--public-key <KEY>")]
        [Description("The public key in authorized_keys format. Not with --public-key-file")]
        public string? PublicKey { get; init; }

        [CommandOption("--public-key-file <PATH>")]
        [Description("Read the public key from a file, e.g. ~/.ssh/id_ed25519.pub. Not with --public-key")]
        public string? PublicKeyFile { get; init; }

        [CommandOption("--expires-in <DURATION>")]
        [Description("Delete the key after this long: 30m, 2h, 7d (5m to 30d). Not with --expires-at")]
        public string? ExpiresIn { get; init; }

        [CommandOption("--expires-at <TIMESTAMP>")]
        [Description("Delete the key at this ISO 8601 time. Not with --expires-in. Omit both for a permanent key")]
        public string? ExpiresAt { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrEmpty(PublicKey) == string.IsNullOrEmpty(PublicKeyFile))
                return ValidationResult.Error("Give exactly one of --public-key or --public-key-file.");
            if (!string.IsNullOrEmpty(ExpiresIn) && !string.IsNullOrEmpty(ExpiresAt))
                return ValidationResult.Error("--expires-in and --expires-at cannot be combined.");
            return base.Validate();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var publicKey = ReadPublicKey(settings);
        var body = new Dictionary<string, object> { ["name"] = settings.Name, ["publicKey"] = publicKey };
        var expiresAt = ResolveExpiry(settings);
        if (expiresAt is not null) body["expiresAt"] = expiresAt.Value.ToString("o", CultureInfo.InvariantCulture);

        var client = settings.CreateClient();
        var result = await client.PostAsync("ssh-keys", body);
        Output.Write(result);
        return 0;
    }

    private static string ReadPublicKey(Settings s)
    {
        string key;
        if (!string.IsNullOrEmpty(s.PublicKeyFile))
        {
            var path = s.PublicKeyFile.StartsWith('~')
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), s.PublicKeyFile[1..].TrimStart('/', '\\'))
                : s.PublicKeyFile;
            if (!File.Exists(path))
                throw SliplaneException.Invalid($"--public-key-file: no file at \"{path}\".");
            key = File.ReadAllText(path);
        }
        else
        {
            key = s.PublicKey!;
        }

        // Pointing at id_ed25519 instead of id_ed25519.pub is an easy slip, and this would
        // upload the private half to a third party.
        if (key.Contains("PRIVATE KEY", StringComparison.Ordinal))
            throw SliplaneException.Invalid(
                "That is a private key. Nothing was sent.",
                "Pass the public half instead - the file ending in .pub.");

        return key.Trim();
    }

    private static DateTimeOffset? ResolveExpiry(Settings s)
    {
        if (!string.IsNullOrEmpty(s.ExpiresAt))
        {
            if (!DateTimeOffset.TryParse(s.ExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
                throw SliplaneException.Invalid($"--expires-at \"{s.ExpiresAt}\" is not a timestamp.", "Use ISO 8601, e.g. 2026-10-20T12:00:00Z.");
            return at.ToUniversalTime();
        }
        if (!string.IsNullOrEmpty(s.ExpiresIn))
            return DateTimeOffset.UtcNow + ParseDuration(s.ExpiresIn);
        return null;
    }

    private static TimeSpan ParseDuration(string value)
    {
        var unit = value[^1];
        if (value.Length > 1 && int.TryParse(value[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var n))
        {
            switch (unit)
            {
                case 'm': return TimeSpan.FromMinutes(n);
                case 'h': return TimeSpan.FromHours(n);
                case 'd': return TimeSpan.FromDays(n);
            }
        }
        throw SliplaneException.Invalid($"--expires-in \"{value}\" is not a duration.", "Use a number and a unit: 30m, 2h or 7d.");
    }
}
