using System.ComponentModel;
using Sliplane.Console.Auth;
using Sliplane.Console.Infrastructure;
using Sliplane.Console.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Accounts;

/// <summary>
/// Adds (or replaces) an account through a browser login instead of a pasted API key. The
/// account then behaves like any other: --account is still required everywhere.
/// </summary>
public sealed class LoginAccountCommand : AsyncCommand<LoginAccountCommand.Settings>
{
    public sealed class Settings : LocalSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("Short name for this account, used as --account elsewhere")]
        public string Name { get; init; } = "";

        [CommandOption("--client-id <ID>")]
        [Description("Your OAuth client (oclient_...) from the Sliplane dashboard, with redirect URI http://127.0.0.1:<port>/callback")]
        public required string ClientId { get; init; }

        [CommandOption("--port <PORT>")]
        [Description("Loopback port for the redirect; must match the client's registered URI (default 8976)")]
        public int Port { get; init; } = 8976;

        [CommandOption("--force")]
        [Description("Replace the credentials of an account that already exists")]
        public bool Force { get; init; }

        public override ValidationResult Validate() =>
            string.IsNullOrWhiteSpace(Name) ? ValidationResult.Error("An account name is required.")
            : Port is < 1024 or > 65535 ? ValidationResult.Error("--port must be 1024-65535.")
            : ValidationResult.Success();
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        settings.ApplyOutputMode();
        if (!Interactive.IsTerminal)
            throw SliplaneException.Invalid("A browser login needs a terminal and a browser.",
                $"Use an API key instead: sliplane accounts add {settings.Name} --api-key <key>");

        var config = ConfigStore.Load();
        var name = settings.Name.Trim();
        var existing = config.Accounts.Keys.FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && !settings.Force)
            throw SliplaneException.Invalid($"An account named '{existing}' already exists.", $"sliplane accounts login {existing} --client-id {settings.ClientId} --force");
        name = existing ?? name;

        var credential = await OAuthLogin.LoginAsync(settings.ClientId, settings.Port, TimeSpan.FromMinutes(5));

        using var me = await new SliplaneClient(credential.AccessToken).GetAsync("me");
        var identity = SliplaneIdentity.Describe(me);
        var organization = SliplaneIdentity.Organization(me);

        var store = SecretStoreFactory.Create();
        using (var _ = await FileLock.AcquireAsync(ConfigStore.LockPath, CancellationToken.None))
        {
            store.Set(SecretKeys.Account(name), credential.Serialize());
            config = ConfigStore.Load();
            var keep = config.Accounts.TryGetValue(name, out var old) ? old.DefaultProject : "";
            config.Accounts[name] = new AccountConfig
            {
                Identity = identity, Organization = organization, DefaultProject = keep,
                AddedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            };
            ConfigStore.Save(config);
        }

        Output.WriteObject(new Dictionary<string, object?>
        {
            ["status"] = existing is null ? "added" : "replaced",
            ["name"] = name, ["identity"] = identity, ["organization"] = organization,
            ["auth"] = "browser", ["renews"] = credential.RefreshToken is not null,
            ["secretStore"] = store.Name,
            ["nextStep"] = $"sliplane projects list --account {name}"
        });
        return 0;
    }
}
