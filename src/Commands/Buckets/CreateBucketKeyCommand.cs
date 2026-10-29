using System.ComponentModel;
using Sliplane.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Buckets;

public sealed class CreateBucketKeyCommand : AsyncCommand<CreateBucketKeyCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--bucket-id|--bucket <BUCKET>")]
        [Description("Bucket name or ID")]
        public required string BucketId { get; init; }

        [CommandOption("--name <NAME>")]
        [Description("Human-readable name for this access key")]
        public required string Name { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var bucketId = await Resolve.BucketAsync(client, settings.BucketId);
        var result = await client.PostAsync($"buckets/{bucketId}/keys", new { name = settings.Name });
        Output.Write(result);
        return 0;
    }
}
