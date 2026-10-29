using System.ComponentModel;
using Sliplane.Console.Auth;
using Spectre.Console.Cli;

namespace Sliplane.Console.Infrastructure;

/// <summary>Settings for every command, including the ones that never call the API.</summary>
public class LocalSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("Print raw JSON instead of YAML, for scripting")]
    public bool Json { get; init; }

    [CommandOption("-y|--yes")]
    [Description("Skip confirmation prompts (prompts only ever appear in an interactive terminal)")]
    public bool Yes { get; init; }

    /// <summary>
    /// Commands render through <see cref="Output"/> without knowing their settings, so the
    /// choice is recorded once, here, before anything is written.
    /// </summary>
    public void ApplyOutputMode() => Output.UseJson = Json || Output.PreferJson();

    /// <summary>Asks before something destructive, in a terminal only; scripts and agents are never blocked.</summary>
    public void Confirm(string question)
    {
        if (Yes || !Interactive.IsTerminal) return;
        if (!Spectre.Console.AnsiConsole.Confirm(question, defaultValue: false))
            throw new SliplaneException(ErrorCode.Error, "Cancelled.");
    }
}

/// <summary>Settings for a command that talks to the Sliplane API as one configured account.</summary>
public class ApiSettings : LocalSettings
{
    /// <summary>
    /// Required, but not declared <c>required</c> - a missing value is resolved by
    /// <see cref="AccountResolver"/> so the failure carries the list of configured accounts
    /// instead of Spectre's bare parse error.
    /// </summary>
    [CommandOption("-a|--account <ACCOUNT>")]
    [Description("Account to run against (required; see 'sliplane accounts list')")]
    public string? Account { get; init; }

    /// <summary>Set by <see cref="CreateClient"/>.</summary>
    public ResolvedAccount? ResolvedAccount { get; private set; }

    public SliplaneClient CreateClient()
    {
        ApplyOutputMode();

        var account = AccountResolver.Resolve(Account);
        ResolvedAccount = account;
        return new SliplaneClient(account.ApiKey, account.OrgId)
        {
            Refresh = account.OAuth is null ? null : () => OAuthLogin.RefreshAndStoreAsync(account.Name, account.OAuth)
        };
    }
}
