using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Buckets;

public sealed class UpdateBucketCommand : AsyncCommand<UpdateBucketCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--bucket-id|--bucket <BUCKET>")]
        [Description("Bucket name or ID")]
        public required string BucketId { get; init; }

        [CommandOption("--versioning <BOOL>")]
        [Description("Set to true to enable S3 versioning, false to disable it")]
        public required bool Versioning { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var bucketId = await Resolve.BucketAsync(client, settings.BucketId);
        var result = await client.PatchAsync($"buckets/{bucketId}", new { versioning = settings.Versioning });
        Output.Write(result);
        return 0;
    }
}
