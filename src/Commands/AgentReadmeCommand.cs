using Sliplane.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands;

/// <summary>
/// The manual an agent reads before its first call. Markdown by default so it can be pasted into
/// a system prompt or a CLAUDE.md; <c>--json</c> gives the same rules as data.
/// </summary>
public sealed class AgentReadmeCommand : Command<AgentReadmeCommand.Settings>
{
    public sealed class Settings : LocalSettings;

    public override int Execute(CommandContext context, Settings settings)
    {
        settings.ApplyOutputMode();

        if (settings.Json)
        {
            Output.WriteObject(Structured());
            return 0;
        }

        System.Console.Out.WriteLine(Readme);
        return 0;
    }

    private static Dictionary<string, object?> Structured() => new()
    {
        ["tool"] = "sliplane",
        ["rules"] = new List<string>
        {
            "Always pass --account. There is no default account and no environment variable.",
            "Run 'sliplane accounts list' first if you do not know which accounts exist; ask the human which to use.",
            "On code auth_required, stop and surface the remediation string. Do not retry.",
            "--env replaces the whole environment. Use 'services set-env' to change one variable.",
            "Secret env values read back empty, so they cannot be copied between services.",
            "A service cannot move between a registry image and a repository build; it must be recreated.",
            "Deletes are irreversible. They ask only in an interactive terminal; from a script or agent they run at once. Read the resource back before deleting it.",
            "Projects, services, servers, databases, buckets and credentials can be given by name or id; an ambiguous name is an error, never a guess.",
            "'services ssh' without --command and 'postgres connect' need a terminal. Use 'services ssh --command' and 'postgres query' instead.",
            "Use --json when you are going to parse the output."
        },
        ["exitCodes"] = new Dictionary<string, object?>
        {
            ["0"] = "ok",
            ["1"] = "error - unclassified, report and stop",
            ["2"] = "network - retry once, then stop",
            ["3"] = "auth_required - stop, surface the remediation to a human",
            ["4"] = "not_found - do not retry",
            ["5"] = "rate_limited - back off before retrying",
            ["6"] = "invalid_input - fix the call",
            ["7"] = "no_account - run sliplane accounts list"
        }
    };

    public const string Readme = """
        # sliplane - agent operating manual

        A CLI over the Sliplane API: servers, services, Postgres databases, buckets, registry
        credentials and OAuth clients - plus SSH, tunnels, SQL and bucket files on top of it. Results are YAML on stdout, errors are YAML on stderr, and
        `--json` switches both to JSON. Prompts and warnings go to stderr, so stdout is always
        safe to parse.

        ## Always name the account

        `--account` (short `-a`) is required on every command that touches the API. There is no
        default, no "current" account and no environment variable - this is deliberate. One
        machine holds keys for several Sliplane accounts, and a convenience default is exactly how
        an agent working from a summarized transcript deletes a service in the wrong one.

            sliplane accounts list                     # what is configured here
            sliplane projects list -a work

        If you do not know which account to use, run `sliplane accounts list` and ask the human.
        Never guess when more than one is configured.

        ### Managing accounts

            sliplane accounts add <name> --api-key <key> [--org-id <id>] [--force]
            sliplane accounts list [--check]
            sliplane accounts test <name>
            sliplane accounts remove <name> --yes

        `add` calls `me` with the key before storing it, so a typo fails at setup rather than on
        some later command. The key goes into the OS keystore (DPAPI on Windows, Keychain on
        macOS, libsecret on Linux); only the name, organization id and a label ever reach
        `config.yaml`. `--org-id` is for legacy tokens that need `X-Organization-ID`; scoped keys
        do not need it.

        `list` reports `stored` without touching the network. `--check` calls `me` once per
        account and reports `valid`, `rejected` or `unreachable` instead.

        `remove` deletes the key from this machine. It does **not** revoke it at Sliplane - say so
        when you report the result, because the key keeps working anywhere else it was copied.

        ## Reading

            sliplane me -a <account>
            sliplane projects list -a <account>
            sliplane servers list -a <account>
            sliplane servers get --server-id <id> -a <account>
            sliplane services list -a <account> [--project-id <id>]
            sliplane services get --project-id <id> --service-id <id> -a <account>
            sliplane services logs --project-id <id> --service-id <id> -a <account>
            sliplane services events --project-id <id> --service-id <id> -a <account>
            sliplane postgres list -a <account>
            sliplane buckets list -a <account>

        `services list` without `--project-id` walks every project, which is one API call per
        project. Pass the project when you know it.

        **Names work wherever an id does.** `--project`, `--service`, `--server`, `--postgres`,
        `--bucket` and `--credential` (aliases of the `-id` options) take a name or an id. An
        exact id wins; otherwise exactly one resource must carry that exact name, or the command
        fails listing the candidates. A service without `--project` is looked up in every
        project.

            sliplane services get --service api -a work
            sliplane services logs --service api --since 15m -a work
            sliplane services logs --service api -f -a work           # follow until Ctrl+C
            sliplane services domains --service api -a work           # custom domains and status

        `logs --follow` prints one line per entry (`--json`: one JSON object per line), oldest
        first. `--since` takes 30s/15m/2h/1d or an ISO time. The API returns at most 500 lines
        per call; a warning on stderr says when that limit was hit.

        ## Changing things

            sliplane services create --project-id <id> --name <name> --server-id <id> ...
            sliplane services update --project-id <id> --service-id <id> ...
            sliplane services deploy --project-id <id> --service-id <id> [--tag <tag>]
            sliplane services set-env --project-id <id> --service-id <id> --key K --value V [--secret]
            sliplane services delete-env --project-id <id> --service-id <id> --key K
            sliplane services pause|unpause --project-id <id> --service-id <id>
            sliplane services attach-volume --project-id <id> --service-id <id> (--volume-id <id> | --name <n>) --mount-path /data
            sliplane services update-volume --project-id <id> --service-id <id> --volume-id <id> --mount-path /data
            sliplane services detach-volume --project-id <id> --service-id <id> --volume-id <id>
            sliplane servers rename-volume --server-id <id> --volume-id <id> --name <n>

        Volume changes on a service redeploy it. `detach-volume` keeps the data on the server;
        `attach-volume --name` reuses the server's volume of that name if there is one, and only
        creates a new volume when there is not.

        ## Metrics

            sliplane servers metrics --server-id <id> -a <account>
            sliplane services metrics --project-id <id> --service-id <id> -a <account>
            sliplane postgres metrics --postgres-id <id> -a <account>

        All three take `--range` (10min, 1h, 24h; servers and services also 7d) or `--from`/`--to`
        in Unix seconds, never both. With neither, the last hour.

        ## Shell, commands and tunnels into a service

            sliplane services ssh --service api --command "ls -la /data" -a <account>
            sliplane services ssh --service api -a <account>                    # interactive shell (terminal only)
            sliplane services ssh --service db -N -L 5432:localhost:5432 -a <account>
            sliplane services tunnel --service admin [--local-port 8080] -a <account>

        Each run creates a temporary SSH key, attaches it, waits until it is accepted, runs the
        system `ssh`, then detaches and deletes the key. `--ttl` (default 1h) is only the expiry
        that removes the key if the CLI is killed first. `--command` exits with the remote
        command's exit code. `tunnel` forwards 127.0.0.1:<port> to the service's internal port and
        runs until Ctrl+C. Needs the OpenSSH client (built into Windows 10+).

        ## Postgres: SQL without psql

            sliplane postgres query --postgres main "select count(*) from users" -a <account>
            sliplane postgres query --postgres main -f migration.sql -a <account>
            sliplane postgres connect --postgres main -a <account>             # interactive (terminal only)
            sliplane postgres allowlist list|add|remove|clear --postgres main -a <account>
            sliplane me --ip -a <account>                                      # this machine's public IPs

        `query` runs as the database owner with credentials fetched from the API (never printed)
        and prints the rows; several statements give one entry each. Before connecting it checks
        this machine's public IP against the allow list and fails with the fix if it is blocked;
        `--allow-my-ip` adds it instead (the rule stays). Writes run for real - there is no dry run.

        ## Bucket files

            sliplane buckets ls --bucket uploads [--prefix a/] [--recursive] -a <account>
            sliplane buckets upload --bucket uploads --file ./x.png [--key a/x.png] -a <account>
            sliplane buckets download --bucket uploads --key a/x.png [-o ./x.png | -o -] -a <account>
            sliplane buckets rm --bucket uploads --key a/x.png -a <account>

        The first file command on a bucket creates an access key named after this machine and
        keeps it in the OS keystore; later commands reuse it.

        ## Presets

            sliplane presets list [--tag database]
            sliplane presets deploy n8n --server main [--project prod] [--name flows] [--env K=V] -a <account>

        A preset is the dashboard's one-click service. Every deploy is a new service with fresh
        generated names and credentials; secret values are shown only with `--show-secrets`, and
        cannot be read back afterwards.

        ## Preferences and housekeeping

            sliplane config show | set output json | set project <name> -a <account> | unset <key>
            sliplane accounts login <name> --client-id <oclient_...> [--port 8976]
            sliplane agent init [--yes]            # write a Sliplane section into AGENTS.md
            sliplane version | update [--check]

        A saved project only fills in `services create` and `presets deploy`; it never changes
        which service a lookup finds. `accounts login` is a browser login through an OAuth client
        you registered in the dashboard (redirect `http://127.0.0.1:<port>/callback`); the token
        renews itself and the account is used like any other, `--account` still required.

        ## SSH keys you manage yourself

            sliplane ssh-keys list -a <account>
            sliplane ssh-keys create --name <n> --public-key-file ~/.ssh/id_ed25519.pub [--expires-in 2h] -a <account>
            sliplane services attach-ssh-key --project-id <id> --service-id <id> --key-id <id> -a <account>
            sliplane services ssh-keys --project-id <id> --service-id <id> -a <account>
            sliplane services detach-ssh-key --project-id <id> --service-id <id> --key-id <id> -a <account>
            sliplane ssh-keys delete --key-id <id> -a <account>

        SSH keys belong to the user the account's API key acts as, not to the organization.
        `attach-ssh-key` prints the `host`, `port` and `username` to connect with. For access
        you only need for a while, prefer `--expires-in` (5m to 30d): the key is deleted and its
        access removed when it runs out. `ssh-keys delete` answers `409` while the key is still
        attached anywhere - detach it first. `create` refuses a private key outright.

        Four behaviours that will otherwise cost you data:

        1. **`--env` replaces the entire environment.** It is not a merge - the API takes the
           array it is given, so passing two variables to a service that holds three deletes the
           third. The CLI refuses such an update and names what would be lost; `--replace-env`
           says you meant it. To change one variable use `services set-env`, which leaves the
           rest alone.
        2. **Secret values are write-only.** They read back empty, so they cannot be copied from
           one service to another and no merge can preserve them.
        3. **A service cannot move between a registry image and a repository build.** The API
           answers `409`. The service has to be deleted and recreated; volumes are server-level
           resources and survive, so data on them is not lost.
        4. **`--branch`, `--dockerfile` and `--docker-context` work on their own**, applied to
           the service's current repository. After the update the CLI re-reads the service and
           fails if the change did not take effect.

        Deletes (`services delete`, `projects delete`, `servers delete`, `postgres delete`,
        `buckets delete`, ...) are irreversible. In an interactive terminal they ask first (`--yes`
        skips that); from a script or an agent they run at once. Read the resource back first and
        confirm with the human that it is the one they meant - including which account it is in.

        ## Errors

        Failures print YAML (or JSON) on stderr with a stable `code`, and exit with a matching
        status:

            0  ok
            1  error          unclassified - report it and stop
            2  network        retry once, then stop
            3  auth_required  stop; give the human the `remediation` string verbatim
            4  not_found      the id does not exist; do not retry
            5  rate_limited   back off before trying again
            6  invalid_input  fix the call
            7  no_account     run `sliplane accounts list`

        An envelope carries `remediation` when there is a specific command that fixes the
        problem. Surface it rather than guessing, and never retry an `auth_required`.

        ## Platform notes

        **Windows: run from PowerShell or cmd, not Git Bash.** Git Bash rewrites arguments that
        look like Unix paths before this program sees them, so `--healthcheck /` arrives as
        `C:/Program Files/Git/`. `MSYS_NO_PATHCONV=1` does not prevent it. The CLI detects the
        result and refuses, rather than deploying a service whose healthchecks can never pass.

        **`--version` belongs to the CLI.** The PostgreSQL major version on `postgres create` is
        `--pg-version`.
        """;
}
