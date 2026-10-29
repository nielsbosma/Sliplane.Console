using System.ComponentModel;
using System.Text;
using Sliplane.Console.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands;

/// <summary>
/// Writes a short Sliplane section into the project's agent instructions, between markers, so a
/// rerun updates it in place and everything outside the markers is left byte for byte.
/// </summary>
public sealed class AgentInitCommand : Command<AgentInitCommand.Settings>
{
    private const string Begin = "<!-- sliplane-console:begin -->";
    private const string End = "<!-- sliplane-console:end -->";

    public sealed class Settings : LocalSettings
    {
        [CommandOption("--file <PATH>")]
        [Description("File to update (default: AGENTS.md, or CLAUDE.md if only that exists, in the current folder)")]
        public string? File { get; init; }
    }

    private const string Section = """
        ## Sliplane

        This project is hosted on Sliplane. Use the `sliplane` CLI (Sliplane.Console) for it, and
        read `sliplane agent-readme` before the first call: it is the CLI's manual for agents.

        - Every command needs `--account <name>`; there is no default. `sliplane accounts list`
          shows the configured accounts. Ask which one if it is not written below.
        - Services, servers, databases and buckets can be named instead of given by id.
        - Add `--json` when parsing output. `sliplane services logs --service <name> --since 15m`
          reads recent logs; `sliplane postgres query --postgres <db> "<sql>"` runs SQL.
        - Deletes and `--env` (which replaces the whole environment) are not undoable; read the
          resource back first and prefer `services set-env` for one variable.
        """;

    public override int Execute(CommandContext context, Settings settings)
    {
        settings.ApplyOutputMode();
        var path = settings.File ?? (!System.IO.File.Exists("AGENTS.md") && System.IO.File.Exists("CLAUDE.md") ? "CLAUDE.md" : "AGENTS.md");
        var full = Path.GetFullPath(path);

        if (System.IO.File.Exists(full) && System.IO.File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint))
            throw SliplaneException.Invalid($"'{path}' is a link; refusing to write through it.");

        var existing = System.IO.File.Exists(full) ? System.IO.File.ReadAllText(full) : "";
        var block = $"{Begin}\n{Section.Replace("\r\n", "\n").TrimEnd()}\n{End}";

        string updated;
        var start = existing.IndexOf(Begin, StringComparison.Ordinal);
        var end = existing.IndexOf(End, StringComparison.Ordinal);
        if (start >= 0 && end > start && existing.IndexOf(Begin, start + 1, StringComparison.Ordinal) < 0)
            updated = existing[..start] + block + existing[(end + End.Length)..];
        else if (start >= 0 || end >= 0)
            throw SliplaneException.Invalid($"'{path}' has unmatched or repeated sliplane-console markers; fix them by hand first.");
        else
            updated = existing.Length == 0 ? block + "\n" : existing.TrimEnd('\n', '\r') + "\n\n" + block + "\n";

        if (updated == existing)
        {
            Output.WriteObject(new Dictionary<string, object?> { ["file"] = full, ["status"] = "unchanged" });
            return 0;
        }

        if (!settings.Yes)
        {
            if (!Interactive.IsTerminal)
                throw SliplaneException.Invalid($"Writing {path} needs confirmation and there is no terminal.", "Pass --yes.");
            if (!AnsiConsole.Confirm($"{(existing.Length == 0 ? "Create" : "Update the Sliplane section in")} {path}?", true))
                throw new SliplaneException(ErrorCode.Error, "Cancelled.");
        }

        System.IO.File.WriteAllText(full, updated, new UTF8Encoding(false));
        Output.WriteObject(new Dictionary<string, object?> { ["file"] = full, ["status"] = existing.Length == 0 ? "created" : start >= 0 ? "updated" : "appended" });
        return 0;
    }
}
