using System.ComponentModel;
using Sliplane.Console.Auth;
using Sliplane.Console.Infrastructure;
using Sliplane.Console.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Config;

public sealed class UnsetConfigCommand : AsyncCommand<UnsetConfigCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandArgument(0, "<KEY>")]
        [Description("output, or project (needs --account)")]
        public required string Key { get; init; }

        public override ValidationResult Validate() =>
            Key.ToLowerInvariant() is "output" or "project"
                ? ValidationResult.Success()
                : ValidationResult.Error("KEY must be output or project");
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        settings.ApplyOutputMode();
        using var _ = await FileLock.AcquireAsync(ConfigStore.LockPath, CancellationToken.None);
        var config = ConfigStore.Load();

        if (settings.Key.Equals("output", StringComparison.OrdinalIgnoreCase))
            config.Preferences.Output = "";
        else
            config.Accounts[AccountResolver.Resolve(settings.Account).Name].DefaultProject = "";

        ConfigStore.Save(config);
        Output.WriteObject(new Dictionary<string, object?> { ["removed"] = settings.Key.ToLowerInvariant() });
        return 0;
    }
}
