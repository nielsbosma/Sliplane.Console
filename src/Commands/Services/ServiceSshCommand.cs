using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Services;

public sealed class ServiceSshCommand : AsyncCommand<ServiceSshCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--project-id|--project <PROJECT>")]
        [Description("Project name or ID (optional: the service is looked up in every project)")]
        public string? ProjectId { get; init; }

        [CommandOption("--service-id|--service <SERVICE>")]
        [Description("Service name or ID")]
        public required string ServiceId { get; init; }

        [CommandOption("-c|--command <COMMAND>")]
        [Description("Run this command in the container and exit with its exit code, instead of opening a shell")]
        public string? Command { get; init; }

        [CommandOption("-L|--local-forward <FORWARD>")]
        [Description("Forward [bind_address:]port:host:hostport through the service, as ssh -L (repeatable)")]
        public string[]? LocalForwards { get; init; }

        [CommandOption("-N|--no-shell")]
        [Description("Keep the forwards open without a shell (needs -L)")]
        public bool NoShell { get; init; }

        [CommandOption("--ttl <DURATION>")]
        [Description("Fallback lifetime of the temporary key if cleanup cannot run (default 1h, 6m to 30d)")]
        public string Ttl { get; init; } = "1h";

        public override ValidationResult Validate()
        {
            if (NoShell && (LocalForwards is null || LocalForwards.Length == 0))
                return ValidationResult.Error("-N needs at least one -L forward.");
            if (NoShell && Command is not null)
                return ValidationResult.Error("-N and --command cannot be combined.");
            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var ttl = SshSession.ParseTtl(settings.Ttl);
        var client = settings.CreateClient();
        var (projectId, service) = await Resolve.ServiceMatchAsync(client, settings.ProjectId, settings.ServiceId);

        var args = new List<string>();
        foreach (var forward in settings.LocalForwards ?? []) { args.Add("-L"); args.Add(forward); }
        if (settings.NoShell) args.Add("-N");

        var shell = settings.Command is null && !settings.NoShell;
        if (shell && !Interactive.IsTerminal)
            throw SliplaneException.Invalid("An interactive shell needs a terminal.",
                $"Run one command instead: sliplane services ssh --service {service.Name} --command \"<cmd>\" -a {settings.ResolvedAccount!.Name}");

        if (shell)
            System.Console.Error.WriteLine($"Connecting to {service.Name}...");
        else if (settings.NoShell)
            System.Console.Error.WriteLine($"Forwarding through {service.Name} (Ctrl+C to stop)...");

        return await SshSession.RunAsync(client, new SshSession.Target(projectId, service.Id, service.Name),
            ttl, args, settings.Command, tty: shell);
    }
}
