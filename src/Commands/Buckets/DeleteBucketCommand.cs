using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Buckets;

public sealed class DeleteBucketCommand : AsyncCommand<DeleteBucketCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--bucket-id|--bucket <BUCKET>")]
        [Description("Bucket name or ID to delete")]
        public required string BucketId { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var bucketId = await Resolve.BucketAsync(client, settings.BucketId);
        settings.Confirm($"Schedule bucket {bucketId} for deletion?");
        var result = await client.DeleteWithResponseAsync($"buckets/{bucketId}");
        Output.Write(result);
        return 0;
    }
}
