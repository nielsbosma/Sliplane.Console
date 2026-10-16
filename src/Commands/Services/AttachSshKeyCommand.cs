using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Services;

public sealed class AttachSshKeyCommand : AsyncCommand<AttachSshKeyCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--project-id <ID>")]
        [Description("The ID of the project")]
        public required string ProjectId { get; init; }

        [CommandOption("--service-id <ID>")]
        [Description("The ID of the service")]
        public required string ServiceId { get; init; }

        [CommandOption("--key-id <ID>")]
        [Description("The ID of an SSH key from 'ssh-keys list'")]
        public required string KeyId { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var result = await client.PutAsync(
            $"projects/{settings.ProjectId}/services/{settings.ServiceId}/ssh-keys/{settings.KeyId}",
            new { });
        Output.Write(result);
        return 0;
    }
}
