# Sliplane CLI

A command-line tool for the [Sliplane](https://sliplane.io) API. Manage your servers, services, projects, and more from the terminal.

## Installation

```bash
dotnet tool install -g Sliplane.Console
```

## Output

Commands print YAML by default. Pass `--json` for raw JSON, which is what you
want when scripting:

```bash
sliplane services get --project-id p --service-id s --json | jq -r .status
```

## Notes on behaviour

**Prompts happen only in a terminal.** Deletes ask for confirmation, and `services create` /
`presets deploy` offer a project or server to pick when none is given - but only when a person is
at an interactive terminal. With redirected input or output (scripts, agents, CI) nothing ever
waits for an answer: deletes run as before, and a missing choice is an error naming the options.
`--yes` skips the confirmation in a terminal too.

**SSH, tunnels, SQL and bucket files talk to the resource itself.** `services ssh`/`tunnel` use
the system OpenSSH client with a key that exists only for the session. `postgres query`/`connect`
use credentials fetched from the API and check this machine's IP against the allow list first.
Bucket file commands create one access key per machine and bucket, kept in the OS keystore.

**`--env` replaces the whole environment.** It is not a merge - the API takes
the array it is given. Passing two variables to a service that holds three
deletes the third. The CLI refuses such an update and names what would be lost;
pass `--replace-env` if removal is what you meant. To change one variable, use
`services set-env`, which leaves the rest alone.

**`--branch`, `--dockerfile` and `--docker-context` work on their own.** They are
applied to the service's current repository, so `services update --branch main`
is enough. They are refused with `--image` or on a service that runs a registry
image. After the update the CLI checks the service the API returns, and fails if
a requested branch, Dockerfile or context did not take effect.

**Secret values are write-only.** They read back as `''`, so they cannot be
copied from one service to another, and a merge could not preserve them.

**A service cannot move between a registry image and a repository build.** The
API returns 409. Delete and recreate it instead - volumes are server-level
resources and survive, so nothing on them is lost.

**Volume names resolve to an existing volume.** `--volume my-data:/data` attaches
the volume already called `my-data` rather than creating another one beside it.

**Windows: run from PowerShell or cmd, not Git Bash.** Git Bash rewrites
arguments that look like Unix paths before this program sees them, so
`--healthcheck /` arrives as `C:/Program Files/Git/`. `MSYS_NO_PATHCONV=1` does
not prevent it. The CLI detects the result and refuses rather than deploying a
service whose healthchecks can never pass.

## Accounts

One machine usually holds keys for more than one Sliplane account, so keys are configured by
name and the name is required on every command. Add an account once:

```bash
sliplane accounts add work --api-key sl_your_api_key
sliplane accounts add side-project        # prompts for the key, without echo
```

`add` calls `me` with the key before storing it, so a bad key fails here rather than on some
later command. The key itself goes into the OS keystore - DPAPI on Windows, Keychain on macOS,
libsecret on Linux - and only the name, organization and a label are written to `config.yaml`.
For legacy tokens that need `X-Organization-ID`, pass `--org-id`.

Then name the account on every command:

```bash
sliplane projects list --account work
sliplane services deploy --project-id p --service-id s -a work
```

There is no default account and no `SLIPLANE_API_KEY`: with keys for several accounts on one
machine, a default is how a deploy ends up in the wrong one.

```bash
sliplane accounts list [--check]    # --check calls the API once per account
sliplane accounts test work         # what this key is, and whether it still works
sliplane accounts remove work       # deletes the local key; does not revoke it at Sliplane
```

| Variable | Effect |
| --- | --- |
| `SLIPLANE_CONFIG_DIR` | Overrides where `config.yaml` and the secret store live |
| `SLIPLANE_SECRET_STORE` | Forces a backend: `dpapi`, `keychain`, `libsecret`, `plaintext` |
| `SLIPLANE_ALLOW_PLAINTEXT_STORE=1` | Permits a 0600 file where no OS keystore exists |

## Driving this from an agent

```bash
sliplane agent-readme           # the operating manual, as markdown
sliplane agent-readme --json    # the same rules and exit codes as data
```

Errors print a YAML (or JSON) envelope on stderr with a stable `code`, and the exit status
matches it: `1` error, `2` network, `3` auth_required, `4` not_found, `5` rate_limited,
`6` invalid_input, `7` no_account. An envelope carries `remediation` when a specific command
fixes the problem.

## Usage

```bash
sliplane <command> [options]
```

### Commands

Every command below takes `--account <name>` (short `-a`), except `accounts *`,
`agent-readme`, `agent init`, `presets list`, `version`, `update` and `config show`/`set output`.

Wherever a command takes `--project-id`, `--service-id`, `--server-id`, `--postgres-id`,
`--bucket-id` or `--credential-id`, it also accepts `--project`, `--service`, ... and a **name**
instead of an id. A service is looked up in every project when no project is given; an
ambiguous name fails and lists the candidates.

| Command | Description |
|---------|-------------|
| `me` | Get current identity and token context |
| `agent-readme` | Print the operating manual for an LLM agent |
| `agent init` | Add or update a Sliplane section in AGENTS.md/CLAUDE.md |
| `version` | Show the installed version |
| `update` | Update the installed tool from NuGet (`--check` to only look) |
| **Config** | |
| `config show` | Show saved preferences |
| `config set` | Save `output yaml` or `output json`, or a default `project` for an account |
| `config unset` | Remove a saved preference |
| **Accounts** | |
| `accounts add` | Add an account and store its API key in the OS keystore |
| `accounts login` | Add an account through a browser login (your own OAuth client) |
| `accounts list` | List configured accounts |
| `accounts test` | Check that an account's stored key still works |
| `accounts remove` | Remove an account and delete its stored key |
| **Projects** | |
| `projects list` | List all projects |
| `projects create` | Create a new project |
| `projects update` | Update a project name |
| `projects delete` | Delete a project |
| **Servers** | |
| `servers list` | List all servers |
| `servers get` | Get server details |
| `servers create` | Create a new server |
| `servers delete` | Delete a server |
| `servers rescale` | Rescale a server (scale up only) |
| `servers rescale-disk` | Grow a server's data disk |
| `servers metrics` | Get server metrics; `--watch` |
| `servers volumes` | List server volumes |
| `servers create-volume` | Create a volume on a server |
| `servers rename-volume` | Rename a volume |
| **Services** | |
| `services list` | List services in a project |
| `services get` | Get service details |
| `services create` | Create a new service |
| `services update` | Update a service |
| `services delete` | Delete a service |
| `services pause` | Pause a service |
| `services unpause` | Unpause a service |
| `services deploy` | Trigger a deployment |
| `services logs` | Get service logs; `--since 15m`, `--follow` |
| `services metrics` | Get service metrics; `--watch` for live charts |
| `services events` | Get service events |
| `services list-env` | List environment variables |
| `services set-env` | Create or replace an environment variable |
| `services delete-env` | Delete an environment variable |
| `services add-domain` | Add a custom domain |
| `services remove-domain` | Remove a custom domain |
| `services attach-volume` | Attach an existing or new volume (redeploys) |
| `services update-volume` | Change a volume's mount path (redeploys) |
| `services detach-volume` | Detach a volume, keeping its data (redeploys) |
| `services ssh` | Shell, `--command`, or `-L` forwards in a service, with a temporary key |
| `services tunnel` | Forward a local port to a service's internal port |
| `services domains` | List managed, internal and custom domains with status |
| `services ssh-keys` | List your SSH keys attached to a service |
| `services attach-ssh-key` | Attach an SSH key and get the SSH connection details |
| `services detach-ssh-key` | Detach an SSH key from a service |
| **Presets** | |
| `presets list` | List the dashboard's one-click presets |
| `presets deploy` | Create a service from a preset |
| **Postgres** | |
| `postgres list` | List all Postgres databases |
| `postgres get` | Get Postgres database details |
| `postgres create` | Create a new Postgres database |
| `postgres update` | Rename, rescale, or set the IP allow list |
| `postgres delete` | Delete a Postgres database |
| `postgres pause` | Pause a Postgres database |
| `postgres unpause` | Unpause a paused Postgres database |
| `postgres restart` | Restart a Postgres database |
| `postgres rotate-credentials` | Rotate generated credentials |
| `postgres query` | Run SQL (argument, `--file` or stdin) and print the rows |
| `postgres connect` | Interactive SQL shell, no psql needed |
| `postgres allowlist list`, `add`, `remove`, `clear` | Manage the IP allow list one rule at a time |
| `postgres metrics` | Get CPU, memory, disk and connection metrics; `--watch` |
| `postgres logs` | List recent server logs |
| `postgres connections` | List current active connections |
| `postgres relation-sizes` | List current table and index sizes |
| `postgres slow-queries` | List current slow queries |
| `postgres top-queries` | List current top queries by call count |
| `postgres restore-window` | Get the point-in-time restore window |
| `postgres restore` | Restore to a point in time (creates a new database) |
| **Buckets** | |
| `buckets list` | List all buckets |
| `buckets create` | Create an S3-compatible bucket |
| `buckets update` | Enable or disable bucket versioning |
| `buckets delete` | Schedule a bucket for deletion |
| `buckets cors` | Get the CORS configuration |
| `buckets set-cors` | Replace the CORS configuration |
| `buckets delete-cors` | Delete the CORS configuration |
| `buckets ls` | List files (one level, or `--recursive`) |
| `buckets upload` | Upload a file |
| `buckets download` | Download a file to a path or stdout |
| `buckets rm` | Delete a file |
| `buckets keys` | List bucket access keys |
| `buckets create-key` | Create a bucket access key |
| `buckets delete-key` | Delete a bucket access key |
| **SSH keys** | |
| `ssh-keys list` | List your SSH public keys |
| `ssh-keys create` | Register an SSH public key, optionally expiring |
| `ssh-keys delete` | Delete an SSH key (detach it from services first) |
| **Credentials** | |
| `credentials list` | List all registry credentials |
| `credentials get` | Get registry credentials details |
| `credentials create` | Create registry credentials |
| `credentials update` | Update registry credentials name |
| `credentials delete` | Delete registry credentials |
| **OAuth** | |
| `oauth list` | List OAuth clients |
| `oauth get` | Get OAuth client details |
| `oauth update` | Update OAuth client metadata |
| `oauth users` | List OAuth client authorized users |

### Examples

```bash
# Configure an account once
sliplane accounts add work --api-key sl_your_api_key

# Check your identity
sliplane me -a work

# List all projects
sliplane projects list -a work

# Create a server
sliplane servers create --name my-server --instance-type base --location ger -a work

# Deploy a service (names work wherever ids do)
sliplane services deploy --service api -a work

# Follow its logs
sliplane services logs --service api --since 10m -f -a work

# Run a command in it, or open a shell
sliplane services ssh --service api --command "df -h" -a work
sliplane services ssh --service api -a work

# Reach a private service from this machine
sliplane services tunnel --service admin --local-port 8080 -a work

# Query a database without psql
sliplane postgres query --postgres main "select now()" -a work

# Copy a file into a bucket
sliplane buckets upload --bucket uploads --file ./logo.png --key img/logo.png -a work

# One-click deploy from the preset catalog
sliplane presets deploy n8n --server main --name flows -a work

# Create a Postgres database
sliplane postgres create --name my-db --instance-type base --region ger -a work

# Restrict a database to a single network
sliplane postgres update --postgres-id pg_abc123 --ip-allow 203.0.113.0/24=office -a work

# Create a bucket and an access key for it
sliplane buckets create --name my-app-uploads --region ger -a work
sliplane buckets create-key --bucket-id sb_abc123 --name "Production uploads" -a work
```

> The PostgreSQL major version on `postgres create` is `--pg-version`; `--version` is reserved by the CLI itself.

## License

MIT
