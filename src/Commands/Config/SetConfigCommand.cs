using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Sliplane.Console.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Config;

/// <summary>
/// <c>output</c> is global. <c>project</c> is saved per account and only fills in where a
/// command creates something (services create, deploy) - it never redirects a lookup.
/// </summary>
public sealed class SetConfigCommand : AsyncCommand<SetConfigCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandArgument(0, "<KEY>")]
        [Description("output (yaml|json) or project (needs --account)")]
        public required string Key { get; init; }

        [CommandArgument(1, "<VALUE>")]
        [Description("The value to save")]
        public required string Value { get; init; }

        public override ValidationResult Validate() => Key.ToLowerInvariant() switch
        {
            "output" when Value is "yaml" or "json" => ValidationResult.Success(),
            "output" => ValidationResult.Error("output must be yaml or json"),
            "project" => ValidationResult.Success(),
            _ => ValidationResult.Error("KEY must be output or project")
        };
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        settings.ApplyOutputMode();
        var isOutput = settings.Key.Equals("output", StringComparison.OrdinalIgnoreCase);
        string? projectId = null;
        if (!isOutput)
        {
            var client = settings.CreateClient();
            projectId = await Resolve.ProjectAsync(client, settings.Value);
        }

        using var _ = await FileLock.AcquireAsync(ConfigStore.LockPath, CancellationToken.None);
        var config = ConfigStore.Load();
        if (isOutput)
            config.Preferences.Output = settings.Value;
        else
            config.Accounts[settings.ResolvedAccount!.Name].DefaultProject = projectId!;

        ConfigStore.Save(config);
        Output.WriteObject(new Dictionary<string, object?> { ["saved"] = settings.Key.ToLowerInvariant(), ["path"] = ConfigStore.ConfigPath });
        return 0;
    }
}
