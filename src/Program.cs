using Sliplane.Console.Commands;
using Sliplane.Console.Commands.Accounts;
using Sliplane.Console.Commands.Buckets;
using Sliplane.Console.Commands.Config;
using Sliplane.Console.Commands.Credentials;
using Sliplane.Console.Commands.OAuth;
using Sliplane.Console.Commands.Postgres;
using Sliplane.Console.Commands.Presets;
using Sliplane.Console.Commands.Projects;
using Sliplane.Console.Commands.Servers;
using Sliplane.Console.Commands.Services;
using Sliplane.Console.Commands.SshKeys;
using Sliplane.Console.Infrastructure;
using Spectre.Console.Cli;

// On Windows the console defaults to the OEM code page (CP850 on a Swedish install), so piped
// output turns "ä" into a byte that is invalid UTF-8 for jq or any JSON reader. stdout, stderr
// and stdin are UTF-8 everywhere.
System.Console.OutputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
System.Console.InputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

// The error envelope is rendered before any command has run, so the format has to be known
// before Spectre parses anything. Commands overwrite this with the same value.
Output.UseJson = args.Contains("--json") || Output.PreferJson();

var app = new CommandApp();

app.Configure(config =>
{
    config.SetExceptionHandler((ex, _) => ErrorOutput.Write(ex));

    config.AddCommand<MeCommand>("me")
        .WithDescription("Get current identity and token context");

    config.AddCommand<AgentReadmeCommand>("agent-readme")
        .WithDescription("Print the operating manual for an LLM agent driving this CLI");

    config.AddBranch("agent", agent =>
    {
        agent.SetDescription("Set up Sliplane guidance for coding agents");
        agent.AddCommand<AgentInitCommand>("init")
            .WithDescription("Add or update a Sliplane section in AGENTS.md/CLAUDE.md in the current folder");
    });

    config.AddCommand<VersionCommand>("version")
        .WithDescription("Show the installed version");

    config.AddCommand<UpdateCommand>("update")
        .WithDescription("Update the installed tool from NuGet (--check to only look)");

    config.AddBranch("config", cfg =>
    {
        cfg.SetDescription("Saved preferences: output format, and a default project per account");
        cfg.AddCommand<ShowConfigCommand>("show")
            .WithDescription("Show saved preferences and the config file path");
        cfg.AddCommand<SetConfigCommand>("set")
            .WithDescription("Save 'output yaml|json', or 'project <name>' for --account");
        cfg.AddCommand<UnsetConfigCommand>("unset")
            .WithDescription("Remove a saved preference");
    });

    config.AddBranch("accounts", accounts =>
    {
        accounts.SetDescription("Manage Sliplane accounts and their API keys");
        accounts.AddCommand<AddAccountCommand>("add")
            .WithDescription("Add an account and store its API key in the OS keystore");
        accounts.AddCommand<LoginAccountCommand>("login")
            .WithDescription("Add an account through a browser login with your own OAuth client");
        accounts.AddCommand<ListAccountsCommand>("list")
            .WithDescription("List configured accounts");
        accounts.AddCommand<TestAccountCommand>("test")
            .WithDescription("Check that an account's stored key still works");
        accounts.AddCommand<RemoveAccountCommand>("remove")
            .WithDescription("Remove an account and delete its stored key");
    });

    config.AddBranch("projects", projects =>
    {
        projects.SetDescription("Manage projects");
        projects.AddCommand<ListProjectsCommand>("list")
            .WithDescription("List all projects");
        projects.AddCommand<CreateProjectCommand>("create")
            .WithDescription("Create a new project");
        projects.AddCommand<UpdateProjectCommand>("update")
            .WithDescription("Update a project name");
        projects.AddCommand<DeleteProjectCommand>("delete")
            .WithDescription("Delete a project");
    });

    config.AddBranch("servers", servers =>
    {
        servers.SetDescription("Manage servers");
        servers.AddCommand<ListServersCommand>("list")
            .WithDescription("List all servers");
        servers.AddCommand<GetServerCommand>("get")
            .WithDescription("Get server details");
        servers.AddCommand<CreateServerCommand>("create")
            .WithDescription("Create a new server");
        servers.AddCommand<DeleteServerCommand>("delete")
            .WithDescription("Delete a server");
        servers.AddCommand<RescaleServerCommand>("rescale")
            .WithDescription("Rescale a server (scale up only)");
        servers.AddCommand<ServerMetricsCommand>("metrics")
            .WithDescription("Get server metrics");
        servers.AddCommand<RescaleServerDiskCommand>("rescale-disk")
            .WithDescription("Rescale a server's data disk (grow only)");
        servers.AddCommand<ListServerVolumesCommand>("volumes")
            .WithDescription("List server volumes");
        servers.AddCommand<CreateServerVolumeCommand>("create-volume")
            .WithDescription("Create a volume on a server");
        servers.AddCommand<RenameServerVolumeCommand>("rename-volume")
            .WithDescription("Rename a volume (services are not redeployed)");
    });

    config.AddBranch("services", services =>
    {
        services.SetDescription("Manage services");
        services.AddCommand<ListServicesCommand>("list")
            .WithDescription("List services in a project");
        services.AddCommand<GetServiceCommand>("get")
            .WithDescription("Get service details");
        services.AddCommand<CreateServiceCommand>("create")
            .WithDescription("Create a new service");
        services.AddCommand<UpdateServiceCommand>("update")
            .WithDescription("Update a service");
        services.AddCommand<DeleteServiceCommand>("delete")
            .WithDescription("Delete a service");
        services.AddCommand<PauseServiceCommand>("pause")
            .WithDescription("Pause a service");
        services.AddCommand<UnpauseServiceCommand>("unpause")
            .WithDescription("Unpause a service");
        services.AddCommand<DeployServiceCommand>("deploy")
            .WithDescription("Trigger a deployment");
        services.AddCommand<ServiceLogsCommand>("logs")
            .WithDescription("Get service logs");
        services.AddCommand<ServiceMetricsCommand>("metrics")
            .WithDescription("Get service metrics");
        services.AddCommand<ServiceEventsCommand>("events")
            .WithDescription("Get service events");
        services.AddCommand<ListServiceEnvCommand>("list-env")
            .WithDescription("List service environment variables");
        services.AddCommand<SetServiceEnvCommand>("set-env")
            .WithDescription("Create or replace a service environment variable");
        services.AddCommand<DeleteServiceEnvCommand>("delete-env")
            .WithDescription("Delete a service environment variable");
        services.AddCommand<AddDomainCommand>("add-domain")
            .WithDescription("Add a custom domain");
        services.AddCommand<RemoveDomainCommand>("remove-domain")
            .WithDescription("Remove a custom domain");
        services.AddCommand<AttachVolumeCommand>("attach-volume")
            .WithDescription("Attach an existing or new volume and redeploy");
        services.AddCommand<UpdateVolumeMountCommand>("update-volume")
            .WithDescription("Change where an attached volume is mounted and redeploy");
        services.AddCommand<DetachVolumeCommand>("detach-volume")
            .WithDescription("Detach a volume (its data is kept) and redeploy");
        services.AddCommand<ServiceSshCommand>("ssh")
            .WithDescription("Open a shell, run a command or forward ports in a service, with a temporary SSH key");
        services.AddCommand<ServiceTunnelCommand>("tunnel")
            .WithDescription("Forward a local port to a service's internal port, with a temporary SSH key");
        services.AddCommand<ListServiceDomainsCommand>("domains")
            .WithDescription("List a service's managed, internal and custom domains with their status");
        services.AddCommand<ListServiceSshKeysCommand>("ssh-keys")
            .WithDescription("List your SSH keys attached to a service");
        services.AddCommand<AttachSshKeyCommand>("attach-ssh-key")
            .WithDescription("Attach an SSH key and print the SSH connection details");
        services.AddCommand<DetachSshKeyCommand>("detach-ssh-key")
            .WithDescription("Detach an SSH key from a service");
    });

    config.AddBranch("presets", presets =>
    {
        presets.SetDescription("The dashboard's one-click service presets");
        presets.AddCommand<ListPresetsCommand>("list")
            .WithDescription("List available presets from Sliplane's public catalog");
        presets.AddCommand<DeployPresetCommand>("deploy")
            .WithDescription("Create a service from a preset, with fresh generated credentials");
    });

    config.AddBranch("postgres", postgres =>
    {
        postgres.SetDescription("Manage managed Postgres databases");
        postgres.AddCommand<ListPostgresCommand>("list")
            .WithDescription("List all Postgres databases");
        postgres.AddCommand<GetPostgresCommand>("get")
            .WithDescription("Get Postgres database details");
        postgres.AddCommand<CreatePostgresCommand>("create")
            .WithDescription("Create a new Postgres database");
        postgres.AddCommand<UpdatePostgresCommand>("update")
            .WithDescription("Update a Postgres database (rename, rescale, IP allow list)");
        postgres.AddCommand<DeletePostgresCommand>("delete")
            .WithDescription("Delete a Postgres database");
        postgres.AddCommand<PausePostgresCommand>("pause")
            .WithDescription("Pause a Postgres database");
        postgres.AddCommand<UnpausePostgresCommand>("unpause")
            .WithDescription("Unpause a paused Postgres database");
        postgres.AddCommand<RestartPostgresCommand>("restart")
            .WithDescription("Restart a Postgres database");
        postgres.AddCommand<RotatePostgresCredentialsCommand>("rotate-credentials")
            .WithDescription("Rotate generated Postgres credentials");
        postgres.AddCommand<PostgresMetricsCommand>("metrics")
            .WithDescription("Get CPU, memory, disk and connection metrics");
        postgres.AddCommand<PostgresLogsCommand>("logs")
            .WithDescription("List recent Postgres server logs");
        postgres.AddCommand<PostgresActiveConnectionsCommand>("connections")
            .WithDescription("List current active connections");
        postgres.AddCommand<PostgresRelationSizesCommand>("relation-sizes")
            .WithDescription("List current table and index sizes");
        postgres.AddCommand<PostgresSlowQueriesCommand>("slow-queries")
            .WithDescription("List current slow queries");
        postgres.AddCommand<PostgresTopQueriesCommand>("top-queries")
            .WithDescription("List current top queries by call count");
        postgres.AddCommand<PostgresQueryCommand>("query")
            .WithDescription("Run SQL (argument, --file or stdin) and print the rows; no psql needed");
        postgres.AddCommand<PostgresConnectCommand>("connect")
            .WithDescription("Open an interactive SQL shell; no psql needed");
        postgres.AddBranch("allowlist", allowlist =>
        {
            allowlist.SetDescription("Manage the database's IP allow list one rule at a time");
            allowlist.AddCommand<ListPostgresAllowlistCommand>("list")
                .WithDescription("List the allowed IP ranges");
            allowlist.AddCommand<AddPostgresAllowlistCommand>("add")
                .WithDescription("Allow an IP or CIDR range, or this machine's IP with --my-ip");
            allowlist.AddCommand<RemovePostgresAllowlistCommand>("remove")
                .WithDescription("Remove one rule by source or id");
            allowlist.AddCommand<ClearPostgresAllowlistCommand>("clear")
                .WithDescription("Remove every rule, blocking all public connections");
        });
        postgres.AddCommand<PostgresRestoreWindowCommand>("restore-window")
            .WithDescription("Get the point-in-time restore window");
        postgres.AddCommand<RestorePostgresCommand>("restore")
            .WithDescription("Restore a database to a point in time (creates a new database)");
    });

    config.AddBranch("buckets", buckets =>
    {
        buckets.SetDescription("Manage S3-compatible object storage buckets");
        buckets.AddCommand<ListBucketsCommand>("list")
            .WithDescription("List all buckets");
        buckets.AddCommand<CreateBucketCommand>("create")
            .WithDescription("Create a bucket");
        buckets.AddCommand<UpdateBucketCommand>("update")
            .WithDescription("Enable or disable bucket versioning");
        buckets.AddCommand<DeleteBucketCommand>("delete")
            .WithDescription("Schedule a bucket for deletion");
        buckets.AddCommand<GetBucketCorsCommand>("cors")
            .WithDescription("Get the bucket CORS configuration");
        buckets.AddCommand<SetBucketCorsCommand>("set-cors")
            .WithDescription("Replace the bucket CORS configuration");
        buckets.AddCommand<DeleteBucketCorsCommand>("delete-cors")
            .WithDescription("Delete the bucket CORS configuration");
        buckets.AddCommand<ListBucketFilesCommand>("ls")
            .WithDescription("List files in a bucket (one folder level, or --recursive)");
        buckets.AddCommand<UploadBucketFileCommand>("upload")
            .WithDescription("Upload a local file");
        buckets.AddCommand<DownloadBucketFileCommand>("download")
            .WithDescription("Download a file (to a path or stdout)");
        buckets.AddCommand<RemoveBucketFileCommand>("rm")
            .WithDescription("Delete a file");
        buckets.AddCommand<ListBucketKeysCommand>("keys")
            .WithDescription("List bucket access keys");
        buckets.AddCommand<CreateBucketKeyCommand>("create-key")
            .WithDescription("Create a bucket access key");
        buckets.AddCommand<DeleteBucketKeyCommand>("delete-key")
            .WithDescription("Delete a bucket access key");
    });

    config.AddBranch("ssh-keys", sshKeys =>
    {
        sshKeys.SetDescription("Manage your SSH public keys for service access");
        sshKeys.AddCommand<ListSshKeysCommand>("list")
            .WithDescription("List your SSH keys");
        sshKeys.AddCommand<CreateSshKeyCommand>("create")
            .WithDescription("Register an SSH public key, optionally expiring");
        sshKeys.AddCommand<DeleteSshKeyCommand>("delete")
            .WithDescription("Delete an SSH key (detach it from services first)");
    });

    config.AddBranch("credentials", credentials =>
    {
        credentials.SetDescription("Manage registry credentials");
        credentials.AddCommand<ListCredentialsCommand>("list")
            .WithDescription("List all registry credentials");
        credentials.AddCommand<GetCredentialsCommand>("get")
            .WithDescription("Get registry credentials details");
        credentials.AddCommand<CreateCredentialsCommand>("create")
            .WithDescription("Create registry credentials");
        credentials.AddCommand<UpdateCredentialsCommand>("update")
            .WithDescription("Update registry credentials name");
        credentials.AddCommand<DeleteCredentialsCommand>("delete")
            .WithDescription("Delete registry credentials");
    });

    config.AddBranch("oauth", oauth =>
    {
        oauth.SetDescription("Manage OAuth clients");
        oauth.AddCommand<ListOAuthClientsCommand>("list")
            .WithDescription("List OAuth clients");
        oauth.AddCommand<GetOAuthClientCommand>("get")
            .WithDescription("Get OAuth client details");
        oauth.AddCommand<UpdateOAuthClientCommand>("update")
            .WithDescription("Update OAuth client metadata");
        oauth.AddCommand<ListOAuthClientUsersCommand>("users")
            .WithDescription("List OAuth client authorized users");
    });
});

return app.Run(args);
