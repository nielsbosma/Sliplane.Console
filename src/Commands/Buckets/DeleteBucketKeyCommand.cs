using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Buckets;

public sealed class DeleteBucketKeyCommand : AsyncCommand<DeleteBucketKeyCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--bucket-id|--bucket <BUCKET>")]
        [Description("Bucket name or ID")]
        public required string BucketId { get; init; }

        [CommandOption("--key-id <ID>")]
        [Description("The ID of the access key to delete")]
        public required string KeyId { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var bucketId = await Resolve.BucketAsync(client, settings.BucketId);
        settings.Confirm($"Revoke key {settings.KeyId} of bucket {bucketId}?");
        await client.DeleteAsync($"buckets/{bucketId}/keys/{settings.KeyId}");
        AnsiConsole.MarkupLine("[green]Access key deleted.[/]");
        return 0;
    }
}
