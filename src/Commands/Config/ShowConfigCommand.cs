using Sliplane.Console.Infrastructure;
using Sliplane.Console.Storage;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Config;

public sealed class ShowConfigCommand : Command<LocalSettings>
{
    public override int Execute(CommandContext context, LocalSettings settings)
    {
        settings.ApplyOutputMode();
        var config = ConfigStore.Load();
        Output.WriteObject(new Dictionary<string, object?>
        {
            ["path"] = ConfigStore.ConfigPath,
            ["output"] = string.IsNullOrEmpty(config.Preferences.Output) ? "yaml" : config.Preferences.Output,
            ["defaultProjects"] = config.Accounts
                .Where(kv => !string.IsNullOrEmpty(kv.Value.DefaultProject))
                .ToDictionary(kv => kv.Key, kv => kv.Value.DefaultProject)
        });
        return 0;
    }
}
