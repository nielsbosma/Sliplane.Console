using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Buckets;

public sealed class ListBucketKeysCommand : AsyncCommand<ListBucketKeysCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--bucket-id|--bucket <BUCKET>")]
        [Description("Bucket name or ID")]
        public required string BucketId { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var bucketId = await Resolve.BucketAsync(client, settings.BucketId);
        var result = await client.GetAsync($"buckets/{bucketId}/keys");
        Output.Write(result);
        return 0;
    }
}
