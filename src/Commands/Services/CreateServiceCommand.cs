using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Services;

public sealed class CreateServiceCommand : AsyncCommand<CreateServiceCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--project-id|--project <PROJECT>")]
        [Description("Project name or ID (default: 'sliplane config set project')")]
        public string? ProjectId { get; init; }

        [CommandOption("--name <NAME>")]
        [Description("The name of the service")]
        public required string Name { get; init; }

        [CommandOption("--server-id|--server <SERVER>")]
        [Description("Server name or ID to run the service on (default: the only server, or asked in a terminal)")]
        public string? ServerId { get; init; }

        [CommandOption("--image <URL>")]
        [Description("Container image URL (e.g. docker.io/library/nginx:latest)")]
        public string? Image { get; init; }

        [CommandOption("--repo <URL>")]
        [Description("Repository URL (e.g. https://github.com/user/repo)")]
        public string? Repo { get; init; }

        [CommandOption("--branch <BRANCH>")]
        [Description("Branch to deploy from (default: main)")]
        public string? Branch { get; init; }

        [CommandOption("--dockerfile <PATH>")]
        [Description("Path to Dockerfile (default: Dockerfile)")]
        public string? DockerfilePath { get; init; }

        [CommandOption("--docker-context <PATH>")]
        [Description("Docker build context (default: .)")]
        public string? DockerContext { get; init; }

        [CommandOption("--auto-deploy")]
        [Description("Auto-deploy on push (default: true)")]
        public bool? AutoDeploy { get; init; }

        [CommandOption("--deploy-include-paths <PATTERN>")]
        [Description("Only deploy if changes in these paths (repeatable)")]
        public string[]? DeployIncludePaths { get; init; }

        [CommandOption("--deploy-ignore-paths <PATTERN>")]
        [Description("Skip deploy if changes only in these paths (repeatable)")]
        public string[]? DeployIgnorePaths { get; init; }

        [CommandOption("--registry-credential-id <ID>")]
        [Description("Registry credential ID for private images")]
        public string? RegistryCredentialId { get; init; }

        [CommandOption("--public")]
        [Description("Make the service publicly accessible")]
        public bool Public { get; init; }

        [CommandOption("--protocol <PROTOCOL>")]
        [Description("Network protocol: http, tcp, udp")]
        public string? Protocol { get; init; }

        [CommandOption("--healthcheck <PATH>")]
        [Description("Health check path (default: /)")]
        public string? Healthcheck { get; init; }

        [CommandOption("--cmd <CMD>")]
        [Description("Override Docker CMD")]
        public string? Cmd { get; init; }

        [CommandOption("--env <KEY=VALUE>")]
        [Description("Environment variable KEY=value, or KEY to copy it from your environment (repeatable)")]
        public string[]? Env { get; init; }

        [CommandOption("--secret-env <KEY=VALUE>")]
        [Description("Secret environment variable KEY=value, or KEY to copy it from your environment (repeatable)")]
        public string[]? SecretEnv { get; init; }

        [CommandOption("--env-file <FILE>")]
        [Description("Read KEY=value lines from a file, stored as secrets; applied before --env (repeatable)")]
        public string[]? EnvFiles { get; init; }

        [CommandOption("--volume <VOLUME>")]
        [Description("Volume mount as id:/path or name:/path (repeatable)")]
        public string[]? Volumes { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var projectId = await Resolve.ProjectOrDefaultAsync(client, settings, settings.ProjectId);
        var serverId = await Resolve.ServerOrChooseAsync(client, settings.ServerId);
        var body = await BuildBodyAsync(client, settings, serverId);
        var result = await client.PostAsync($"projects/{projectId}/services", body);
        Output.Write(result);
        return 0;
    }

    private static async Task<Dictionary<string, object>> BuildBodyAsync(SliplaneClient client, Settings s, string serverId)
    {
        var body = new Dictionary<string, object>
        {
            ["name"] = s.Name,
            ["serverId"] = serverId
        };

        if (!string.IsNullOrEmpty(s.Image))
        {
            var deployment = new Dictionary<string, object> { ["url"] = s.Image };
            if (!string.IsNullOrEmpty(s.RegistryCredentialId))
                deployment["registryAuthenticationId"] = s.RegistryCredentialId;
            body["deployment"] = deployment;
        }
        else if (!string.IsNullOrEmpty(s.Repo))
        {
            var deployment = new Dictionary<string, object> { ["url"] = s.Repo };
            if (!string.IsNullOrEmpty(s.Branch)) deployment["branch"] = s.Branch;
            if (!string.IsNullOrEmpty(s.DockerfilePath)) deployment["dockerfilePath"] = s.DockerfilePath;
            if (!string.IsNullOrEmpty(s.DockerContext)) deployment["dockerContext"] = s.DockerContext;
            if (s.AutoDeploy.HasValue) deployment["autoDeploy"] = s.AutoDeploy.Value;
            if (s.DeployIncludePaths is not null) deployment["includePaths"] = s.DeployIncludePaths;
            if (s.DeployIgnorePaths is not null) deployment["ignorePaths"] = s.DeployIgnorePaths;
            body["deployment"] = deployment;
        }

        var network = new Dictionary<string, object> { ["public"] = s.Public };
        if (!string.IsNullOrEmpty(s.Protocol)) network["protocol"] = s.Protocol;
        body["network"] = network;

        if (!string.IsNullOrEmpty(s.Healthcheck)) body["healthcheck"] = PathArg.Check(s.Healthcheck, "--healthcheck");
        if (!string.IsNullOrEmpty(s.Cmd)) body["cmd"] = s.Cmd;

        var envVars = EnvEntries.Build(s.EnvFiles, s.Env, s.SecretEnv);
        if (envVars.Count > 0) body["env"] = envVars;

        if (s.Volumes is not null)
        {
            // Names resolve to existing volumes first; see ServerVolumes.
            Dictionary<string, string>? byName = null;
            if (s.Volumes.Any(v => !v.Split(':', 2)[0].StartsWith("volume_")))
                byName = await ServerVolumes.ByNameAsync(client, serverId);

            var volumes = new List<Dictionary<string, object>>();
            foreach (var v in s.Volumes)
            {
                var parts = v.Split(':', 2);
                var vol = new Dictionary<string, object> { ["mountPath"] = PathArg.Check(parts[1], "--volume") };
                if (parts[0].StartsWith("volume_"))
                    vol["id"] = parts[0];
                else if (byName is not null && byName.TryGetValue(parts[0], out var id))
                    vol["id"] = id;
                else
                    vol["name"] = parts[0];
                volumes.Add(vol);
            }
            body["volumes"] = volumes;
        }

        return body;
    }
}
